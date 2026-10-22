# Custom Music

The **Custom Music** page (sidebar, under Patches) lists every piece of music Retro Rewind can play. Drop a song on a row
(or use the row's menu) and it replaces that music. Changes are only written to the game when you press **Save**.

## How music works in Mario Kart Wii and Retro Rewind

| What | Where it comes from |
| --- | --- |
| Original course and arena music | `sound/strm/*.brstm` on the disc. One file per course, plus a separate final lap file (`n_Circuit32_n.brstm` and `n_Circuit32_f.brstm`). Capitalisation varies (`n_Kinoko_F`, `STRM_N_KOOPA_N`), and the game wants the exact names. |
| Menus, results, fanfares, credits | Also BRSTM streams in `sound/strm` (`o_Wi-Fi_waiting32`, `o_FanfareGP1_32`, ...). |
| Retro Rewind tracks | Pulsar looks for `strm/<file name>_n.brstm` (then `_N`), using the creator file name from `Binaries/Config*.pul`. The final lap uses `_final` or `_F`. Tracks without their own music use the music slot of an original course. |
| Retro Rewind menus | Pulsar's own `title_bg`, `offline_bg`, `wifi_globe_bg` and `wifi_lobby_bg`. |
| Sound effects, voices, sequenced jingles | Packed in `revo_kart.brsar`. The BRSAR also keeps a table of the streams, with each one's size and channel count. |
| "My Stuff" style overrides | Retro Rewind loads loose files from `RetroRewind6/Patches` and matches them by **file name only**, so a `n_Circuit32_n.brstm` in there replaces Luigi Circuit's music. Wheel Wizard copies every enabled patch from the Patches page into that folder when it launches the game. |

**Lap 3 speed-up.** Retro Rewind's *Music speedup* setting (in its sound settings) chooses between two behaviours:

- **Off** (the default): on the final lap the game switches to the final lap file (`_f`/`_F`, or `_final` for Retro
  Rewind tracks). If a Retro Rewind track only has an `_n` file, that file plays 6% faster and higher (pitch ×1.06).
- **On**: the game keeps playing the normal file and raises its pitch by about 6% over the first seconds of the final
  lap. The final lap file is not used.

Galaxy Colosseum always uses the speed-up. Custom Music writes both files, so either setting works. You choose how the
final lap file sounds in the page's settings (gear icon):

- **Faster**: like Retro Rewind does it, 6% faster and higher.
- **Faster, same pitch**: 8% faster tempo with the key unchanged.
- **Unchanged**: the same file for every lap.

**The BrawlBox "silence halfway through a lap" bug.** The original game trusts the stream size and channel count in
`revo_kart.brsar`. A replacement BRSTM that is longer, or has fewer channels, than the entry says made the game read past
the end of the file and stop the music. Retro Rewind's Pulsar engine patches both checks at runtime (`BRSARPatches.cpp`),
so a loose BRSTM can have any length. Custom Music also writes standard 2-channel, 32 kHz DSP-ADPCM files with
block-aligned loops, the same layout as Nintendo's files and VGAudio's.

## What happens to a dropped song

1. **Decode.** MP3, WAV, FLAC, OGG, AIFF and BRSTM are decoded by built-in code. M4A/AAC, Opus, WMA and similar formats
   are decoded too when FFmpeg is installed on the PC; Wheel Wizard never downloads FFmpeg itself.
2. **Tags.** The title, artist and cover art are read from ID3, Vorbis comments, FLAC pictures and MP4 tags; for
   formats only FFmpeg understands (Opus, WebM, WMA, ...) FFmpeg reads them. A picture with the song's own name next
   to it (`song.jpg`, as yt-dlp saves thumbnails) is used too. Generic album art (`cover.jpg`, `folder.jpg`, ...) is
   only used for songs tagged with an album, so a loose download never borrows another album's cover. Rows with custom
   music get a coloured background and show the cover.
3. **Loudness matching.** The music being replaced is measured with ITU-R BS.1770 (integrated LUFS), either from
   Retro Rewind's files or, for the original tracks, from the game disc image chosen in Wheel Wizard. The new song is
   set to the same loudness, with a −1 dBFS peak limiter so loud masters do not clip.
   Retro Rewind also reads a **volume byte** from the header of every music file (offset 0x3F, 1–127) and multiplies
   the music's volume by byte / 127 (no byte = full volume). A replacement gets the original's byte, so both play at
   the same level. When the song needs more level than its peaks allow (a quiet original with a low byte, or
   *Louder*), the byte is raised first, up to 127, and only the rest is left to the limiter, so the song keeps its
   dynamics. If the original has no byte, it already plays at full volume and nothing can be raised.
   The original is measured the way the game plays it: in races only the file's first track is heard (further tracks
   are alternatives such as Koopa Cape's underwater mix), while Retro Rewind plays its own menu music with every
   track at full volume, so those tracks add up. Everything else in the game's volume chain (the sound archive's
   volume for that music, the background music player, the per-track volumes the game sets) applies to the
   original and the replacement alike.
   When the original cannot be measured (no game disc set, unreadable disc image), the target is −16 LUFS, a typical
   level for game music. The row then says "volume estimated", and its tooltip says why.
   *Louder* / *Quieter* (±2 dB) in the row menu convert the song again; clicks made during a conversion add up. The
   row shows the adjustment, and the tooltip shows the loudness reached. A song whose peaks are already at the limit
   cannot get louder without distorting; the page says so instead of silently squashing it further. WiiCompiled has
   its own *Music* volume slider in its settings, which scales all music.
4. **Looping.** Loop points in the file are kept: WAV `smpl` chunks, `LOOPSTART`/`LOOPLENGTH` tags, or a BRSTM's own
   loop. Without them, Wheel Wizard looks for a loop in the music itself: most songs repeat a section, so playback can
   jump from a late repeat back to an earlier one without anyone hearing the cut.
   - The song is cut into 64 ms frames, each described by its energy in 24 frequency bands (log-spaced, ~60 Hz up).
   - For every loop length of at least 12 seconds, each frame is compared with the frame that far earlier (cosine
     similarity of the band shapes, minus a penalty per dB of level difference so a fade-out never matches the full
     version, with silence never matching). The jump point needs about 3 seconds of matching music on both sides.
   - Of the jump points that match well enough (similarity ≥ 0.95), longer loops and later jumps win, so as much of
     the song as possible is heard before it repeats.
   - The frame-level guess is refined to the exact sample by cross-correlating the waveforms around both points, and
     the last 30 ms before the jump are blended into the audio before the loop start so the waveform continues
     without a click.

   Songs that do not repeat cleanly (or are too short) fall back to a crossfade: the ending fades into the opening and
   the loop restarts right after that opening. Either way the loop start is then moved onto a BRSTM block boundary
   (the audio that follows is appended to the loop end, so the loop is unchanged) because the game can only jump to
   the start of a block. Loop points shorter than a second are ignored (the game would repeat a tiny fragment as a
   buzz). Jingles such as start fanfares play once. The row's tooltip shows where the loop came from and where it
   starts and ends.
5. **Encoding.** The song is resampled to 32 kHz and encoded to DSP-ADPCM BRSTM.

## Previewing music

Every row with a playable original has a play button, so you can hear what a track is before replacing it (useful when
several tracks have similar names). The original comes from Retro Rewind's own files, or from the game disc image for
the original game's music; the row menu also has *Play your song* for the converted replacement, exactly as it will be
installed. A player bar at the bottom shows progress (click it to jump to a point), pause and stop.

Playback uses the operating system directly: Windows' `PlaySound`, `afplay` on macOS, and on Linux the first of
`paplay`, `pw-play`, `aplay`, `ffplay`, `play` or `mpv` that is installed. Music is stopped when you leave the page.

Tracks whose names look alike show their file name next to the description, and get it
appended to the name when the names are identical.

## Same course, several consoles

Retro Rewind has many versions of some courses ("SNES Rainbow Road", "N64 Rainbow Road", "Rainbow Road", ...). Rows
whose names match once the console tag, version suffix (`v2`) and notes in brackets are removed form a family, and the
row menu offers *Use for all N Rainbow Road tracks*: the row's song (or a newly picked one) is converted for every
member, each matched to the loudness of its own original.

## Music packs

Music packs are usually zips of BRSTM files named after the game's music (`n_Circuit32_n.brstm`, ...).

- **Installed as a patch.** A pack added on the Patches page (and enabled) shows on the rows it replaces ("Music from
  <pack>"), and the play button plays the pack's version. When two packs provide the same file, the one with the
  higher priority is shown, as in game. A song chosen here still wins, because the Custom Music patch has the highest
  priority.
- **Dropped on the page.** Dropping a pack's zip (or a folder) on the page extracts its audio files into the library and
  matches them to their tracks by file name; BRSTMs keep their loop points and are loudness-matched like any song.
  Archives over 4 GB are refused.

Files extracted straight into Retro Rewind's own `Patches` folder are not seen: Wheel Wizard replaces that folder's
contents with the enabled patches when it launches the game, so packs should be added on the Patches page.

**Remove all custom music** (gear menu) puts every row back to its original music; like every change, it only takes
effect when saved.

## Saving

Pressing **Save** writes the converted files into a patch called **Custom Music** on the Patches page:

- The patch is created if needed, gets the highest priority so it beats music packs, and is switched back on if it was
  disabled.
- Songs that were reset are removed from it, and the patch is deleted when no custom music is left.
- If another enabled patch with a higher priority provides the same file, the page warns about the conflict.

The library (which song belongs to which row, plus the converted files) is stored in Wheel Wizard's data folder under
`CustomMusic/`, so nothing is lost when Retro Rewind updates.

## Development

Run from source with `dotnet run --project WheelWizard`, and the tests with `dotnet test`; the custom music tests are in
`WheelWizard.Test/Features/CustomMusic`. Nothing in the feature is platform specific apart from preview playback (see
above): file names are compared case-insensitively, paths are built with the file system abstraction, and FFmpeg is
looked up on `PATH` (plus Homebrew's folders on macOS).

## Code map

| Folder | Contents |
| --- | --- |
| `Features/CustomMusic/Audio` | Decoders, tag readers, loudness meter, resampler, limiter, DSP-ADPCM encoder, BRSTM reader and writer, loop finder, `MusicConverter` |
| `Features/CustomMusic/Catalog` | Original game music list, Pulsar config and BMG reader, Retro Rewind track discovery, course families |
| `Features/CustomMusic/Library` | Saved songs (`library.json`), reference loudness, game disc reader, music pack import, the installer that writes the patch |
| `Features/CustomMusic/Playback` | Preview player and the per-platform audio output |
| `Features/CustomMusic/ViewModels`, `Views` | The page |
