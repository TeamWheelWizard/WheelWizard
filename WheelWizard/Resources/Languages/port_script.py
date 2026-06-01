from __future__ import annotations

import csv
import datetime as dt
import sys
from pathlib import Path


LANGUAGE_DIR = Path(__file__).resolve().parent
TRANSLATOR_KEY = "value.language.z_translators"
TRANSLATOR_EXPORT_HINT = "Put your name here if you contributed."
NEWLINE_NORMALIZED_IMPORT_KEYS = {"hover.rooms_page_disclaimer"}


def parse_yaml_file(path: Path) -> tuple[str, dict]:
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    root: dict = {}
    stack: list[tuple[int, dict]] = [(-1, root)]
    root_code = path.stem
    index = 0

    while index < len(lines):
        line = lines[index]
        index += 1
        if not line.strip():
            continue

        indent = len(line) - len(line.lstrip(" "))
        stripped = line.strip()
        if ":" not in stripped:
            continue

        key, raw_value = stripped.split(":", 1)
        key = key.strip()
        raw_value = raw_value.strip()

        while stack and indent <= stack[-1][0]:
            stack.pop()

        current = stack[-1][1]
        if raw_value == "":
            child: dict = {}
            current[key] = child
            stack.append((indent, child))
            if indent == 0:
                root_code = key
            continue

        if raw_value == "|-":
            block_lines: list[str] = []
            block_indent = indent + 2
            while index < len(lines):
                next_line = lines[index]
                next_indent = len(next_line) - len(next_line.lstrip(" "))
                if next_line.strip() and next_indent <= indent:
                    break

                index += 1
                if len(next_line) >= block_indent:
                    block_lines.append(next_line[block_indent:])
                else:
                    block_lines.append("")

            current[key] = "\n".join(block_lines)
            continue

        current[key] = unquote_yaml_value(raw_value)

    if root_code in root and isinstance(root[root_code], dict):
        return root_code, root[root_code]

    return root_code, root


def unquote_yaml_value(value: str) -> str:
    value = value.strip()
    if len(value) >= 2 and value[0] == '"' and value[-1] == '"':
        value = value[1:-1]

    result: list[str] = []
    index = 0
    while index < len(value):
        char = value[index]
        if char == "\\" and index + 1 < len(value):
            next_char = value[index + 1]
            if next_char in {'"', "\\"}:
                result.append(next_char)
                index += 2
                continue

        result.append(char)
        index += 1

    return "".join(result)


def write_yaml_file(path: Path, language_code: str, values: dict) -> None:
    lines = [f"{language_code}:"]
    append_yaml_dict(lines, values, 2)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def append_yaml_dict(lines: list[str], values: dict, indent: int) -> None:
    prefix = " " * indent
    for key, value in values.items():
        if isinstance(value, dict):
            lines.append(f"{prefix}{key}:")
            append_yaml_dict(lines, value, indent + 2)
            continue

        text = str(value)
        if "\n" in text or "\r" in text:
            lines.append(f"{prefix}{key}: |-")
            for block_line in text.replace("\r\n", "\n").replace("\r", "\n").split("\n"):
                lines.append(" " * (indent + 2) + block_line)
            continue

        lines.append(f'{prefix}{key}: "{quote_yaml_value(text)}"')


def quote_yaml_value(value: str) -> str:
    return value.replace("\\", "\\\\").replace('"', '\\"')


def flatten(values: dict, prefix: str = "") -> dict[str, str]:
    result: dict[str, str] = {}
    for key, value in values.items():
        full_key = f"{prefix}.{key}" if prefix else key
        if isinstance(value, dict):
            result.update(flatten(value, full_key))
        else:
            result[full_key] = str(value)

    return result


def unflatten(values: dict[str, str]) -> dict:
    root: dict = {}
    for full_key, value in values.items():
        cursor = root
        parts = full_key.split(".")
        for part in parts[:-1]:
            existing = cursor.get(part)
            if not isinstance(existing, dict):
                existing = {}
                cursor[part] = existing
            cursor = existing

        cursor[parts[-1]] = value

    return root


def load_languages() -> dict[str, dict[str, str]]:
    languages: dict[str, dict[str, str]] = {}
    for path in sorted(LANGUAGE_DIR.glob("*.yml")):
        language_code, values = parse_yaml_file(path)
        languages[language_code] = flatten(values)

    return languages


def ordered_language_codes(languages: dict[str, dict[str, str]]) -> list[str]:
    codes = list(languages.keys())
    if "en" in codes:
        codes.remove("en")
        return ["en", *codes]

    return codes


def ordered_translation_keys(languages: dict[str, dict[str, str]]) -> list[str]:
    if "en" not in languages:
        raise RuntimeError("en.yml is required because it is the source of truth for translation keys.")

    return list(languages["en"].keys())


def export_csv() -> Path:
    languages = load_languages()
    language_codes = ordered_language_codes(languages)
    keys = ordered_translation_keys(languages)

    date_text = dt.datetime.now().strftime("%d-%m-%Y")
    output_path = unique_path(LANGUAGE_DIR / f"WheelWizard Translations {date_text}.csv")

    with output_path.open("w", encoding="utf-8-sig", newline="") as file:
        writer = csv.writer(file)
        writer.writerow(["key", *language_codes])
        for key in keys:
            row_values = []
            for code in language_codes:
                if code == "en" and key == TRANSLATOR_KEY:
                    row_values.append(TRANSLATOR_EXPORT_HINT)
                else:
                    row_values.append(languages[code].get(key, ""))

            writer.writerow([key, *row_values])

    return output_path


def unique_path(path: Path) -> Path:
    if not path.exists():
        return path

    stem = path.stem
    suffix = path.suffix
    for index in range(2, 1000):
        candidate = path.with_name(f"{stem} ({index}){suffix}")
        if not candidate.exists():
            return candidate

    raise RuntimeError(f"Could not find an unused file name for {path}")


def import_csv() -> None:
    csv_path = ask_for_csv_path()
    if csv_path is None:
        print("Import cancelled.")
        return

    existing_languages = load_languages()
    source_keys = ordered_translation_keys(existing_languages)
    source_key_set = set(source_keys)

    with csv_path.open("r", encoding="utf-8-sig", newline="") as file:
        reader = csv.DictReader(file)
        if reader.fieldnames is None or "key" not in reader.fieldnames:
            raise RuntimeError("CSV must have a 'key' column.")

        language_codes = [name for name in reader.fieldnames if name != "key" and name]
        for language_code in language_codes:
            existing_languages[language_code] = {
                key: value
                for key, value in existing_languages.setdefault(language_code, {}).items()
                if key in source_key_set and value != ""
            }

        for row in reader:
            key = (row.get("key") or "").strip()
            if not key:
                continue
            if key not in source_key_set:
                continue

            for language_code in language_codes:
                if language_code == "en" and key == TRANSLATOR_KEY:
                    continue

                value = row.get(language_code)
                if value is None or value == "":
                    if language_code != "en" and key in existing_languages[language_code]:
                        del existing_languages[language_code][key]
                    continue

                if language_code == "en" and value == "":
                    continue

                existing_languages[language_code][key] = normalize_import_value(key, value)

    for language_code in ordered_language_codes(existing_languages):
        output_path = LANGUAGE_DIR / f"{language_code}.yml"
        canonical_values = {
            key: existing_languages[language_code][key]
            for key in source_keys
            if existing_languages[language_code].get(key, "") != ""
        }
        if language_code == "en" and TRANSLATOR_KEY in source_key_set:
            canonical_values[TRANSLATOR_KEY] = "-"

        write_yaml_file(output_path, language_code, unflatten(canonical_values))

    print(f"Imported {csv_path}")


def ask_for_csv_path() -> Path | None:
    csv_files = sorted(LANGUAGE_DIR.glob("*.csv"))
    if csv_files:
        print("CSV files in the language folder:")
        for index, path in enumerate(csv_files, start=1):
            print(f"  {index}. {path.name}")
    else:
        print("No CSV files found in the current directory.")

    answer = input("Type a number, paste a full CSV path, or press Enter to cancel: ").strip().strip('"')
    if not answer:
        return None

    if answer.isdigit():
        selected_index = int(answer)
        if selected_index < 1 or selected_index > len(csv_files):
            raise RuntimeError("Selected number is out of range.")
        return csv_files[selected_index - 1]

    path = Path(answer).expanduser()
    if not path.is_absolute():
        path = LANGUAGE_DIR / path

    if not path.exists() or path.suffix.lower() != ".csv":
        raise RuntimeError(f"CSV file not found: {path}")

    return path


def normalize_import_value(key: str, value: str) -> str:
    if key not in NEWLINE_NORMALIZED_IMPORT_KEYS:
        return value

    return value.replace("\\r\\n", "\n").replace("\\n", "\n")


def ask_mode() -> str:
    if len(sys.argv) > 1:
        return sys.argv[1].strip().lower()

    print("WheelWizard translation port script")
    print("  1. Export YAML to CSV")
    print("  2. Import CSV to YAML")
    return input("Choose export or import: ").strip().lower()


def main() -> int:
    try:
        mode = ask_mode()
        if mode in {"1", "e", "export"}:
            output_path = export_csv()
            print(f"Exported {output_path}")
            return 0

        if mode in {"2", "i", "import"}:
            import_csv()
            return 0

        print("Nothing selected.")
        return 1
    except Exception as ex:
        print(f"Error: {ex}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
