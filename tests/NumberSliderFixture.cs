using System;
using System.Globalization;
using System.Text.RegularExpressions;
using EFT.UI;
using UnityEngine;
using TMPro;

namespace TMPro
{
    public sealed class TMP_InputField
    {
        public enum CharacterValidation { Integer, Decimal }
        public string text;
        public bool readOnly;
        public int characterLimit;
        public CharacterValidation characterValidation;
    }
}
namespace UnityEngine
{
    public static class Mathf
    {
        public const float Epsilon = float.Epsilon;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }
}
public static class UnityUtils
{
    public static float RoundFloatValue(float value, int digits)
    {
        float factor = (float)Math.Pow(10, digits);
        return (float)Math.Round(value * factor) / factor;
    }
}
// Models the installed Slider.Set boundary, including callbacks while Show changes
// min before max. Throw before exhausting the process stack in the regression case.
public sealed class SliderHost
{
    private float minimum, maximum = 1, current = 0.5f;
    public Action<float> Changed;
    public int Callbacks;
    public float minValue { get => minimum; set { minimum = value; Set(current); } }
    public float maxValue { get => maximum; set { maximum = value; Set(current); } }
    public float value { get => current; set => Set(value); }
    private void Set(float input)
    {
        float clamped = UnityEngine.Mathf.Clamp(input, minimum, maximum);
        if (current == clamped) return;
        current = clamped;
        if (++Callbacks > 64) throw new InvalidOperationException("recursive value callbacks");
        Changed?.Invoke(clamped);
    }
}
namespace EFT.UI
{
    public partial class NumberSlider
    {
        private readonly SliderHost _slider = new SliderHost();
        private readonly TMPro.TMP_InputField _valueInput = new TMPro.TMP_InputField();
        private string _format;
        private string _cachedOriginalText = string.Empty;
        private float _minValue, _maxValue;
        private Action<float> _onValueChanged;
        public NumberSlider() { _slider.Changed = SetCurrentValue; }
        public float Value => _slider.value;
        public string Text => _valueInput.text;
        public int Callbacks => _slider.Callbacks;
        public void Observe(Action<float> action) { _onValueChanged = action; }
    }
}
public static class NumberSliderChecks
{
    public static void Main(string[] args)
    {
        int checks = 0;
        Action<bool,string> check = (ok,name) => { if (!ok) throw new Exception(name); checks++; };
        bool recursive = false;
        try { new NumberSlider().Show(5, 80, "0'%'"); }
        catch (InvalidOperationException ex) { recursive = ex.Message == "recursive value callbacks"; }
        check(recursive, "Old percent format reproduces the recorded recursive callback failure");
        foreach(int value in new[]{5,10,20,30,45,80})
        {
            var slider = new NumberSlider();
            slider.Show(5,80,args[0]);
            slider.UpdateValue(value,false,5,80);
            check(slider.Value == value, "Configured percentage remains numerically exact");
            check(slider.Text == value.ToString(CultureInfo.InvariantCulture), "Native input is strictly numeric");
            check(slider.Callbacks < 8, "Initialization callbacks terminate");
            float published = -1;
            slider.Observe(v => published = v);
            slider.SetCurrentValue(value);
            check(published == value, "Normal native callback is retained");
            slider.SetStringValue("45");
            check(slider.Value == 45, "Manual numeric editing works");
        }
        Console.WriteLine("Passed " + checks + " native number-slider initialization/editing checks.");
    }
}
