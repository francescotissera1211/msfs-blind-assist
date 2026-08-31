using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using System.Windows.Forms;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The DA40's output-mode readouts.
///
/// These have to be here because BaseAircraftDefinition implements NONE of them — it does
/// a variable-map lookup and nothing else, so every aircraft answers its own readout keys.
/// Until this file existed, B and L simply did nothing on the DA40.
///
/// Everything below answers in the aeroplane's own terms rather than an airliner's:
///   - Flaps read UP / T/O / LDG, the three positions this aeroplane has.
///   - Gear reports FIXED. The DA40 has fixed tricycle gear; saying "gear down" would be
///     technically true and completely useless, and saying nothing would look broken.
///   - Fuel is in US gallons AND litres, because the AFM quotes both and the aeroplane is
///     fuelled in either depending on where you are.
///   - The characteristic-speed keys carry the DA40's V-speeds instead of the Airbus green
///     dot / S / F set, which do not exist here.
///   - The altimeter reads BOTH subscales, because this aeroplane has two and the AFM
///     descent check says "Altimeters (2) SET". Reading only one would hide the other
///     being wrong.
/// </summary>
public partial class CowsDA40Definition
{
    private static readonly string[] FlapDetents = { "UP", "T/O", "LDG" };

    /// <summary>US gallons to litres, for the dual-unit fuel readouts.</summary>
    private const double LitresPerGallon = 3.785411784;

    /// <summary>
    /// Input mode + B: SET both altimeters.
    ///
    /// This aeroplane has two and the AFM descent check is "Altimeters (2) ... SET", so
    /// one prompt sets both — asking twice for the same number would be a chore invented
    /// by the software, not by the aircraft.
    ///
    /// The G1000 subscale is NOT an L:var: it is the stock SimVar, and the aeroplane's own
    /// Logic.xml drives it with `(L:STATE_BARO1) 16 * (>K:KOHLSMAN_SET, Millibars)` — the
    /// UNINDEXED event, in millibars times sixteen. Verified live, including the two ways
    /// that do NOT work: writing `L:KOHLSMAN SETTING HG:1` (a name with a space and a
    /// colon is a stock SimVar, so that just creates a stray L:var nothing reads) and the
    /// indexed `K:2:KOHLSMAN_SET` both left the value untouched.
    ///
    /// The STANDBY one really is an L:var of that shape — `L:KOHLSMAN SETTING HG:2`,
    /// which the model reads back into STATE_BARO2 — so it is written as one. The two
    /// altimeters genuinely take different transports.
    /// </summary>
    private bool HandleDA40BaroSet(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        Form? parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return true;
        }

        var dialog = new Forms.ValueInputForm(
            "Set Altimeters",
            "Altimeter setting",
            "948 to 1066 hectopascals, or 28.00 to 31.50 inches",
            announcer,
            ValidateBaroEntry);

        if (dialog.ShowDialog(parentForm) != DialogResult.OK || !dialog.IsValidInput) return true;
        if (!double.TryParse(dialog.InputValue, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double entered))
        {
            return true;
        }

        // Same convention as the standby panel's own field: the ranges cannot overlap, so
        // magnitude says which unit was meant.
        double inHg = Math.Clamp(entered > 100 ? entered / 33.8639 : entered, 28.00, 31.50);
        double millibars = inHg * 33.8639;

        simConnect.ExecuteCalculatorCode(
            $"{millibars * 16:0.###} (>K:KOHLSMAN_SET)".Replace(",", "."));
        simConnect.SetLVar("KOHLSMAN SETTING HG:2", inHg);

        announcer.AnnounceImmediate(
            $"Both altimeters set, {millibars:0} hectopascals, {inHg:0.00} inches");
        return true;
    }

    private static (bool isValid, string message) ValidateBaroEntry(string text)
    {
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value))
        {
            return (false, "Enter a number.");
        }

        if (value > 100)
        {
            return value is >= 948 and <= 1066
                ? (true, "")
                : (false, "Hectopascals must be between 948 and 1066.");
        }

        return value is >= 28.00 and <= 31.50
            ? (true, "")
            : (false, "Inches must be between 28.00 and 31.50.");
    }

    /// <summary>
    /// Input mode + N: set the NAV standby frequencies.
    ///
    /// On the aeroplane these are tuned with the G1000's NAV knob, and that stays true —
    /// this is the same convenience Ctrl+B is for the altimeters, not a replacement for
    /// the bezel. Both radios are offered in one pass because a pilot tuning an approach
    /// sets the ILS and the missed-approach aid together; either prompt can be cancelled.
    ///
    /// The event is NAV1_STBY_SET_HZ in RAW HERTZ. Verified live, including the two
    /// spellings that do not work: NAV1_STBY_SET with a BCD-ish 11030 left the value at
    /// its previous 113.90, and NAV1_STBY_SET_HZ with a "MHz" unit hint set it to ZERO.
    /// </summary>
    private bool HandleDA40NavRadios(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        Form? parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return true;
        }

        var set = new List<string>();

        foreach (int radio in new[] { 1, 2 })
        {
            var dialog = new Forms.ValueInputForm(
                $"Set NAV {radio} Standby",
                $"NAV {radio} standby frequency",
                "108.00 to 117.95 megahertz",
                announcer,
                ValidateNavFrequency);

            if (dialog.ShowDialog(parentForm) != DialogResult.OK || !dialog.IsValidInput) continue;
            if (!double.TryParse(dialog.InputValue, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double mhz))
            {
                continue;
            }

            long hz = (long)Math.Round(mhz * 1_000_000.0);
            simConnect.ExecuteCalculatorCode($"{hz} (>K:NAV{radio}_STBY_SET_HZ)");
            set.Add($"NAV {radio} standby {mhz:0.00}");
        }

        announcer.AnnounceImmediate(set.Count == 0 ? "No frequency set" : string.Join(", ", set));
        return true;
    }

    private static (bool isValid, string message) ValidateNavFrequency(string text)
    {
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double mhz))
        {
            return (false, "Enter a frequency in megahertz.");
        }

        return mhz is >= 108.00 and <= 117.95
            ? (true, "")
            : (false, "NAV frequencies run from 108.00 to 117.95.");
    }

    private bool HandleDA40Readout(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        var speeds = DA40Speeds.For(_variant);

        switch (action)
        {
            // ---------- B: both altimeters ----------
            case HotkeyAction.ReadAltimeter:
            {
                // Both subscales, BY KEY. The standby one is the Standby panel's own
                // setting control, which is Continuous - not STATE_BARO2, whose mirror is
                // OnRequest and so reads nothing unless that panel happens to be open.
                double? g1000 = ReadNow(simConnect, "DA40_G1000_BARO");
                double? standby = ReadNow(simConnect, "DA40_STBY_ALTIMETER_SET");

                announcer.AnnounceImmediate(
                    $"Altimeter {BaroPhrase(g1000)}. Standby {BaroPhrase(standby)}.");
                return true;
            }

            // ---------- L: flaps ----------
            case HotkeyAction.ReadFlaps:
            {
                double? f = ReadNow(simConnect, "DA40_FLAPS_POSITION");
                if (f is null)
                {
                    announcer.AnnounceImmediate("Flap position not available yet");
                    return true;
                }

                int i = (int)Math.Round(f.Value);
                announcer.AnnounceImmediate(i >= 0 && i < FlapDetents.Length
                    ? $"Flaps {FlapDetents[i]}"
                    : $"Flaps {i}");
                return true;
            }

            // ---------- Shift+G: gear ----------
            case HotkeyAction.ReadGear:
                // Answering honestly beats both silence and a meaningless "gear down".
                announcer.AnnounceImmediate("Fixed gear");
                return true;

            // ---------- F: fuel ----------
            case HotkeyAction.ReadFuelQuantity:
            case HotkeyAction.ReadFuelInfo:
            {
                double? leftOpt = ReadNow(simConnect, "DA40_FUEL_MAIN_ACTUAL");
                double? rightOpt = ReadNow(simConnect, "DA40_FUEL_AUX_ACTUAL");
                if (leftOpt is null || rightOpt is null)
                {
                    announcer.AnnounceImmediate("Fuel quantity not available yet");
                    return true;
                }

                double left = leftOpt.Value;
                double right = rightOpt.Value;
                double total = left + right;

                // The NG's tanks are Main and Auxiliary, not left and right — the AFM is
                // explicit about it, and calling them left/right here would mislead.
                string a = IsNG ? "Main" : "Left";
                string b = IsNG ? "Auxiliary" : "Right";

                announcer.AnnounceImmediate(
                    $"Fuel {total:0.0} gallons, {total * LitresPerGallon:0} litres. " +
                    $"{a} {left:0.0}, {b} {right:0.0} gallons.");
                return true;
            }

            // ---------- W: weight ----------
            case HotkeyAction.ReadGrossWeightKg:
            {
                double? lbOpt = ReadNow(simConnect, "DA40_GROSS_WEIGHT");
                if (lbOpt is null)
                {
                    announcer.AnnounceImmediate("Gross weight not available yet");
                    return true;
                }

                double lb = lbOpt.Value;
                double maxLb = IsNG ? 2888 : 2646;

                announcer.AnnounceImmediate(
                    $"Gross weight {lb * 0.45359237:0} kilograms, {lb:0} pounds. " +
                    $"Maximum {maxLb * 0.45359237:0} kilograms.");
                return true;
            }

            // ---------- Characteristic speeds ----------
            // The Airbus set (green dot / S / F / VLS) does not exist on a DA40, so these
            // keys carry the speeds this aeroplane actually flies, from AFM section 2.
            case HotkeyAction.ReadSpeedVS:
                announcer.AnnounceImmediate(
                    $"Rotate {speeds.Vr:0} knots. Best rate of climb {speeds.VyFlapsTakeoff:0} " +
                    $"with take-off flap, {speeds.VyFlapsUp:0} clean.");
                return true;

            case HotkeyAction.ReadSpeedVLS:
                announcer.AnnounceImmediate(
                    $"Approach {speeds.Vref:0} knots. Short field {speeds.VrefShortField:0}. " +
                    $"Best glide {speeds.VbestGlide:0}.");
                return true;

            case HotkeyAction.ReadSpeedVFE:
            {
                int flap = (int)Math.Round(ReadNow(simConnect, "DA40_FLAPS_POSITION") ?? 0);
                string limit = flap switch
                {
                    1 => $"Take-off flap limit {speeds.VfeTakeoff:0} knots",
                    2 => $"Landing flap limit {speeds.VfeLanding:0} knots",
                    _ => $"Next flap limit {speeds.VfeTakeoff:0} knots"
                };
                announcer.AnnounceImmediate(limit);
                return true;
            }

            case HotkeyAction.ReadSpeedGD:
                announcer.AnnounceImmediate($"Best glide {speeds.VbestGlide:0} knots.");
                return true;

            case HotkeyAction.ReadSpeedS:
                announcer.AnnounceImmediate(
                    $"Manoeuvring {speeds.Va:0} knots. Never exceed {speeds.Vne:0}. " +
                    $"Maximum structural cruise {speeds.Vno:0}.");
                return true;

            case HotkeyAction.ReadSpeedF:
                announcer.AnnounceImmediate(
                    $"Flap limits: take-off {speeds.VfeTakeoff:0}, landing {speeds.VfeLanding:0} knots.");
                return true;
        }

        return false;
    }

    /// <summary>
    /// A barometric setting in BOTH units. ATIS gives one or the other depending where you
    /// are, and converting in your head while flying is not the pilot's job.
    /// </summary>
    private static string BaroPhrase(double? inHg)
        => inHg is null
            ? "not available yet"
            : $"{inHg.Value * 33.8639:0} hectopascals, {inHg.Value:0.00} inches";

    /// <summary>
    /// Reads a variable straight from the cache. Readout hotkeys must answer immediately -
    /// a hotkey that silently queues a request and says nothing looks exactly like a
    /// broken key, which is what flaps and baro did before this file existed.
    ///
    /// The argument is an MSFSBA VARIABLE KEY, never a SimVar name. The cache is written
    /// as lastVariableValues[varKey], so looking up "FUEL TANK LEFT MAIN QUANTITY" or
    /// "KOHLSMAN SETTING HG:1" misses however correct the name is. Every readout here once
    /// did exactly that, and the old "?? 0" then turned the miss into a reading: a full
    /// aeroplane reported "0 hectopascals" and "0.0 gallons".
    ///
    /// It returns null rather than 0 for the same reason. A missing reading and a genuine
    /// zero are different facts, and only the caller knows how to say which it has.
    /// </summary>
    private static double? ReadNow(SimConnectManager simConnect, string key)
        => simConnect.GetCachedVariableValue(key);
}
