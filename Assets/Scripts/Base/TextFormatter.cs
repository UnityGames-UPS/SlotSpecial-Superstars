using System;
using System.Text;
using TMPro;
using UnityEngine;

public static class TextFormatter
{
  // Rounds away-from-zero to exactly 2 decimals (0.255 -> "0.26", 5 -> "5.00", 1234.5 -> "1234.50").
  // Used by the per-digit HUD displays; distinct from FormatMoney which keeps significant decimals.
  public static string FormatMoneyFixed2(double value)
  {
    decimal rounded = Math.Round((decimal)value, 2, MidpointRounding.AwayFromZero);
    return rounded.ToString("F2");
  }

  // Distributes a money value across a row of single-digit TMP_Text slots (the decimal point is a
  // separate static text and is NOT part of slots). Digits are right-aligned; leading unused slots
  // are hidden by setting their text color alpha to 0. The two rightmost slots are always the decimals.
  public static void ApplyMoneyDigits(TMP_Text[] slots, double value)
  {
    if (slots == null || slots.Length == 0) return;

    string digits = FormatMoneyFixed2(value).Replace(".", "");
    int n = slots.Length;
    // If the value has more digits than slots, keep only the rightmost n.
    if (digits.Length > n) digits = digits.Substring(digits.Length - n);
    int offset = n - digits.Length;

    for (int i = 0; i < n; i++)
    {
      TMP_Text t = slots[i];
      if (t == null) continue;
      if (i >= offset)
      {
        t.text = digits[i - offset].ToString();
        SetAlpha(t, 1f);
      }
      else
      {
        SetAlpha(t, 0f);
      }
    }
  }

  private static void SetAlpha(TMP_Text t, float a)
  {
    Color c = t.color;
    c.a = a;
    t.color = c;
  }
  public static int GetSignificantDecimals(double value, int minDecimals = 0)
  {
    string raw = value.ToString("F3");
    int dotIndex = raw.IndexOf('.');
    int places = 0;
    if (dotIndex >= 0)
    {
      string decimals = raw.Substring(dotIndex + 1).TrimEnd('0');
      places = decimals.Length;
    }
    return Mathf.Max(minDecimals, places);
  }

  public static string FormatMoney(double value)
  {
    int places = GetSignificantDecimals(value, 2);
    return value.ToString($"F{places}");
  }

  public static string FormatSprite(double value, int decimalPlaces, bool CustomSpriteSwitch = false)
  {
    string formatted = value.ToString($"F{decimalPlaces}");
    var sb = new StringBuilder();
    foreach (char c in formatted)
    {
      if (c >= '0' && c <= '9') sb.Append($"<sprite={c - '0'}>");
      else if (CustomSpriteSwitch && c == '.') sb.Append("<sprite=10>");
      else if (c == '.') sb.Append("<sprite=11>");
    }
    return sb.ToString();
  }

  // Free-spin count text font: digits at sprite=0..9, slash at sprite=10. Distinct from the
  // line-win/total-win sprite font where '.' lives at sprite=10 (CustomSpriteSwitch=true).
  public static string FormatSpriteFraction(int numerator, int denominator)
  {
    var sb = new StringBuilder();
    foreach (char c in numerator.ToString())
      if (c >= '0' && c <= '9') sb.Append($"<sprite={c - '0'}>");
    sb.Append("<sprite=10>");
    foreach (char c in denominator.ToString())
      if (c >= '0' && c <= '9') sb.Append($"<sprite={c - '0'}>");
    return sb.ToString();
  }
}
