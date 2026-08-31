using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

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
