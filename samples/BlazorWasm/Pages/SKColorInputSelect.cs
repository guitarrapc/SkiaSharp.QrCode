using Microsoft.AspNetCore.Components.Forms;
using SkiaSharp;
using System.Diagnostics.CodeAnalysis;

namespace BlazorWasm.Pages;

public class SKColorInputSelect<TValue> : InputSelect<TValue>
{
    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out TValue result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        if (!string.IsNullOrEmpty(value) && typeof(TValue) == typeof(SKColor))
        {
            if (SKColor.TryParse(value, out var color))
            {
                result = (TValue)(object)color;
                validationErrorMessage = null;
                return true;
            }
            else
            {
                result = default;
                validationErrorMessage = $"The selected value {value} is not a valid SKColor.";
                return false;
            }
        }
        else
        {
            return base.TryParseValueFromString(value, out result, out validationErrorMessage);
        }
    }
}
