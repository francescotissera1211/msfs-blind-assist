using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Audio. Both variants.
///
/// The DA40's audio panel is the GMA 1347 built into the G1000 bezel, so by the standing
/// rule it belongs to the G1000 display window rather than here. What this panel owns is
/// what the audio panel DOES, reachable without it: which radio the microphone is on, and
/// whether the other one is being monitored. Both are stock and both were verified live —
/// COM1_TRANSMIT_SELECT moved COM TRANSMIT:1 from 0 to 1, COM_RECEIVE_ALL_SET moved COM
/// RECEIVE ALL and COM RECEIVE:2 together.
///
/// And the headset jack, which is a real clickspot on the console and the ONLY audio item
/// COWS models itself: L:HEADSET is referenced by exactly one file in the package,
/// sound.xml. It changes what the pilot hears, nothing else — so it is a control here and
/// it is not pretending to be a system.
///
/// The active frequencies ride along on the scan. "Transmitting on COM 1" is half an
/// answer without the number, and a readout is not a control — tuning belongs with the
/// radios, which are still to come.
/// </summary>
public partial class CowsDA40Definition
{
    private const string AudioPanel = "Audio";

    private static Dictionary<string, SimVarDefinition> BuildAudioVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // Bound to COM 2's transmit flag rather than COM 1's so the encoding reads the
        // way the selection does: 0 is COM 1, 1 is COM 2.
        v["DA40_AUDIO_TRANSMIT"] = new SimVarDefinition
        {
            Name = "COM TRANSMIT:2",
            DisplayName = "Transmit Radio",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "COM 1",
                [1] = "COM 2"
            }
        };

        v["DA40_AUDIO_MONITOR_BOTH"] = new SimVarDefinition
        {
            Name = "COM RECEIVE ALL",
            DisplayName = "Monitor Both Radios",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Transmit radio only",
                [1] = "Both"
            },
            HelpText = "Listens to the radio you are not transmitting on as well."
        };

        v["DA40_AUDIO_HEADSET"] = new SimVarDefinition
        {
            Name = "HEADSET",
            DisplayName = "Headset",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Unplugged",
                [1] = "Plugged in"
            },
            HelpText = "The jack on the console. Changes what you hear, nothing else."
        };

        // ---------- Status ----------

        AddComFreq(v, "DA40_AUDIO_COM1_ACTIVE", "COM ACTIVE FREQUENCY:1", "COM 1 Active");
        AddComFreq(v, "DA40_AUDIO_COM2_ACTIVE", "COM ACTIVE FREQUENCY:2", "COM 2 Active");

        AddComFlag(v, "DA40_AUDIO_COM1_RECEIVE", "COM RECEIVE:1", "COM 1 Audio");
        AddComFlag(v, "DA40_AUDIO_COM2_RECEIVE", "COM RECEIVE:2", "COM 2 Audio");

        return v;
    }

    private static void AddComFreq(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "MHz",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            // Three decimals or a whole-MHz frequency loses its fraction and reads as a
            // bare "121" - the same trap the A380 RMP readouts hit.
            Format = "F3"
        };
    }

    private static void AddComFlag(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Muted",
                [1] = "Heard"
            }
        };
    }

    private static readonly List<string> AudioControls = new()
    {
        "DA40_AUDIO_TRANSMIT",
        "DA40_AUDIO_MONITOR_BOTH",
        "DA40_AUDIO_HEADSET"
    };

    private static readonly List<string> AudioDisplay = new()
    {
        "DA40_AUDIO_COM1_ACTIVE",
        "DA40_AUDIO_COM2_ACTIVE",
        "DA40_AUDIO_COM1_RECEIVE",
        "DA40_AUDIO_COM2_RECEIVE"
    };

    private bool HandleAudioSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "DA40_AUDIO_TRANSMIT":
                simConnect.ExecuteCalculatorCode(
                    value >= 0.5 ? "1 (>K:COM2_TRANSMIT_SELECT)" : "1 (>K:COM1_TRANSMIT_SELECT)");
                return true;

            case "DA40_AUDIO_MONITOR_BOTH":
                simConnect.ExecuteCalculatorCode(
                    $"{(value >= 0.5 ? 1 : 0)} (>K:COM_RECEIVE_ALL_SET)");
                return true;

            case "DA40_AUDIO_HEADSET":
                simConnect.SetLVar("HEADSET", value >= 0.5 ? 1 : 0);
                return true;
        }

        return false;
    }
}
