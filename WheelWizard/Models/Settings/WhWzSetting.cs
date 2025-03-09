using System.Text.Json;
using WheelWizard.Services.Settings;

namespace WheelWizard.Models.Settings;

public class WhWzSetting : Setting
{
    public WhWzSetting(Type type, string name, object defaultValue) : base(type, name, defaultValue)
    {
        WhWzSettingManager.Instance.RegisterSetting(this);
    }
    protected override bool SetInternal(object newValue, bool skipSave = false)
    {
        var oldValue = Value;
        Value = newValue;
        var newIsValid = SaveEvenIfNotValid || IsValid();
        if (newIsValid)
        {
            if (!skipSave)
                WhWzSettingManager.Instance.SaveSettings(this);   
        }
        else
            Value = oldValue;
     
        return newIsValid;
    }

    public override object Get() => Value;
    public override bool IsValid() => ValidationFunc == null || ValidationFunc(Value);
    
    public bool SetFromJson(JsonElement newValue, bool skipSave = false)
    {
        // Feel free to add more types if you find them
        return ValueType switch
        {
            { } t when t == typeof(bool) =>     Set(newValue.GetBoolean(), skipSave),
            { } t when t == typeof(int) =>      Set(newValue.GetInt32(), skipSave),
            { } t when t == typeof(long) =>     Set(newValue.GetInt64(), skipSave),
            { } t when t == typeof(float) =>    Set((float)newValue.GetDouble(), skipSave),
            { } t when t == typeof(double) =>   Set(newValue.GetDouble(), skipSave),
            { } t when t == typeof(string) =>   Set(newValue.GetString()!, skipSave),
            { } t when t == typeof(DateTime) => Set(newValue.GetDateTime(), skipSave),
            { IsEnum: true } t =>               Set(Enum.ToObject(t, newValue.GetInt32()), skipSave),
            { IsArray: true } t =>              SetArray(newValue, t.GetElementType()!, skipSave),
            _ => throw new InvalidOperationException($"Unsupported type: {ValueType.Name}")
        };
    }
   
   private bool SetArray(JsonElement value, Type elementType, bool skipSave = false)
   {
       var json = value.GetRawText().Trim('\0'); 
       var arrayType = Array.CreateInstance(elementType, 0).GetType();
       var array = (Array)JsonSerializer.Deserialize(json, arrayType)!;
       return Set(array, skipSave);
   }
}
