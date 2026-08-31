using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Instrument Panel → Radios. Both variants.
///
/// THIS PANEL IS A DELIBERATE EXCEPTION to "anything doable inside a G1000 display does not
/// get a panel". The radios ARE tuned on the G1000 — but only by turning the dual
/// concentric knob, one character at a time, and there is no keyboard on the aeroplane to
/// do it any other way. Making a blind pilot click a knob event per digit to reach 124.80
/// is the kind of thing that rule exists to prevent, not to require. The same judgement as
/// Ctrl+B for the altimeters: the aeroplane's own mechanism stays, and MSFSBA offers a
/// faster road to the identical result.
///
/// TWO RADIOS, TWO DIFFERENT ENCODINGS, on the same aeroplane. This is the trap:
///
///   NAV   NAV{n}_STBY_SET_HZ        RAW HERTZ      110300000 -> 110.30
///   COM   COM{n}_STBY_RADIO_SET     BCD16          9344      -> 124.80
///
/// and the obvious-looking COM1_STBY_RADIO_SET_HZ does NOTHING — measured, the standby
/// stayed at 125.855 while the BCD form moved it immediately. Nor does the NAV pair accept
/// BCD. Both were established by writing and reading back, not by reasoning from the names.
///
/// Everything here sets the STANDBY and then swaps on demand, which is how the radio is
/// actually flown: you tune the next frequency while still talking on the current one.
///
/// A REAL LIMIT, stated rather than hidden: BCD16 carries four digits, so the COM radios
/// take 25 kHz spacing and no finer. 136.975 encodes as 0x3697 and comes back 136.97.
/// That is the only event this radio accepts, so an 8.33 kHz channel cannot be typed here
/// — it has to be dialled on the G1000's own knob.
/// </summary>
public partial class CowsDA40Definition
{
    private const string RadiosPanel = "Radios";

    private static Dictionary<string, SimVarDefinition> BuildRadioVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddStandbySet(v, "DA40_RADIO_COM1_SET", "COM STANDBY FREQUENCY:1", "COM 1 Standby",
            "Type a frequency in megahertz, for example 124.80. 25 kHz spacing.");
        AddSwap(v, "DA40_RADIO_COM1_SWAP", "Swap COM 1");

        AddStandbySet(v, "DA40_RADIO_COM2_SET", "COM STANDBY FREQUENCY:2", "COM 2 Standby",
            "Type a frequency in megahertz. 25 kHz spacing.");
        AddSwap(v, "DA40_RADIO_COM2_SWAP", "Swap COM 2");

        AddStandbySet(v, "DA40_RADIO_NAV1_SET", "NAV STANDBY FREQUENCY:1", "NAV 1 Standby",
            "Type a frequency in megahertz, 108.00 to 117.95.");
        AddSwap(v, "DA40_RADIO_NAV1_SWAP", "Swap NAV 1");

        AddStandbySet(v, "DA40_RADIO_NAV2_SET", "NAV STANDBY FREQUENCY:2", "NAV 2 Standby",
            "Type a frequency in megahertz, 108.00 to 117.95.");
        AddSwap(v, "DA40_RADIO_NAV2_SWAP", "Swap NAV 2");

        // The CRS knob's value. On a G1000 this is the OBS course for the selected NAV,
        // and it is what the course pointer on the HSI is set to.
        v["DA40_RADIO_CRS1_SET"] = new SimVarDefinition
        {
            Name = "NAV OBS:1",
            DisplayName = "NAV 1 Course",
            Type = SimVarType.SimVar,
            Units = "degrees",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = false,
            Format = "F0",
            HelpText = "Course for NAV 1, 0 to 359."
        };

        v["DA40_RADIO_HDG_BUG_SET"] = new SimVarDefinition
        {
            Name = "AUTOPILOT HEADING LOCK DIR",
            DisplayName = "Heading Bug",
            Type = SimVarType.SimVar,
            Units = "degrees",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = false,
            Format = "F0",
            HelpText = "Heading bug, 0 to 359."
        };

        // ---------- Status ----------

        AddFreqReadout(v, "DA40_RADIO_COM1_ACTIVE", "COM ACTIVE FREQUENCY:1", "COM 1 Active");
        AddFreqReadout(v, "DA40_RADIO_COM2_ACTIVE", "COM ACTIVE FREQUENCY:2", "COM 2 Active");
        AddFreqReadout(v, "DA40_RADIO_NAV1_ACTIVE", "NAV ACTIVE FREQUENCY:1", "NAV 1 Active");
        AddFreqReadout(v, "DA40_RADIO_NAV2_ACTIVE", "NAV ACTIVE FREQUENCY:2", "NAV 2 Active");

        return v;
    }

    private static void AddStandbySet(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display, string help)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "MHz",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = false,
            Format = "F3",
            HelpText = help
        };
    }

    private static void AddSwap(Dictionary<string, SimVarDefinition> v, string key, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = key,
            DisplayName = display,
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };
    }

    private static void AddFreqReadout(Dictionary<string, SimVarDefinition> v, string key,
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
            Format = "F3"
        };
    }

    private static readonly List<string> RadioControls = new()
    {
        "DA40_RADIO_COM1_SET",
        "DA40_RADIO_COM1_SWAP",
        "DA40_RADIO_COM2_SET",
        "DA40_RADIO_COM2_SWAP",
        "DA40_RADIO_NAV1_SET",
        "DA40_RADIO_NAV1_SWAP",
        "DA40_RADIO_NAV2_SET",
        "DA40_RADIO_NAV2_SWAP",
        "DA40_RADIO_CRS1_SET",
        "DA40_RADIO_HDG_BUG_SET"
    };

    private static readonly List<string> RadioDisplay = new()
    {
        "DA40_RADIO_COM1_ACTIVE",
        "DA40_RADIO_COM2_ACTIVE",
        "DA40_RADIO_NAV1_ACTIVE",
        "DA40_RADIO_NAV2_ACTIVE"
    };

    /// <summary>124.80 MHz becomes 0x2480, which is 9344. The COM radios take this and
    /// nothing else; see the class comment.</summary>
    public static int ComBcd16(double megahertz)
    {
        // Only the four digits after the leading "1" are encoded: 124.800 -> 2 4 8 0.
        int scaled = (int)Math.Round(megahertz * 1000.0) % 100000;   // 24800
        int d1 = scaled / 10000;                                      // 2
        int d2 = scaled / 1000 % 10;                                  // 4
        int d3 = scaled / 100 % 10;                                   // 8
        int d4 = scaled / 10 % 10;                                    // 0
        return (d1 << 12) | (d2 << 8) | (d3 << 4) | d4;
    }

    private bool HandleRadioSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "DA40_RADIO_COM1_SET":
            case "DA40_RADIO_COM2_SET":
            {
                int radio = varKey.Contains('2') ? 2 : 1;
                double mhz = Math.Clamp(value, 118.0, 136.999);
                string evt = radio == 1 ? "COM_STBY_RADIO_SET" : "COM2_STBY_RADIO_SET";
                simConnect.ExecuteCalculatorCode($"{ComBcd16(mhz)} (>K:{evt})");
                announcer.AnnounceImmediate($"COM {radio} standby {mhz:0.000}");
                return true;
            }

            case "DA40_RADIO_NAV1_SET":
            case "DA40_RADIO_NAV2_SET":
            {
                int radio = varKey.Contains('2') ? 2 : 1;
                double mhz = Math.Clamp(value, 108.0, 117.95);
                long hz = (long)Math.Round(mhz * 1_000_000.0);
                simConnect.ExecuteCalculatorCode($"{hz} (>K:NAV{radio}_STBY_SET_HZ)");
                announcer.AnnounceImmediate($"NAV {radio} standby {mhz:0.00}");
                return true;
            }

            case "DA40_RADIO_COM1_SWAP":
                simConnect.ExecuteCalculatorCode("1 (>K:COM_STBY_RADIO_SWAP)");
                return true;

            case "DA40_RADIO_COM2_SWAP":
                simConnect.ExecuteCalculatorCode("1 (>K:COM2_RADIO_SWAP)");
                return true;

            case "DA40_RADIO_NAV1_SWAP":
                simConnect.ExecuteCalculatorCode("1 (>K:NAV1_RADIO_SWAP)");
                return true;

            case "DA40_RADIO_NAV2_SWAP":
                simConnect.ExecuteCalculatorCode("1 (>K:NAV2_RADIO_SWAP)");
                return true;

            case "DA40_RADIO_CRS1_SET":
            {
                int deg = ((int)Math.Round(value) % 360 + 360) % 360;
                simConnect.ExecuteCalculatorCode($"{deg} (>K:VOR1_SET)");
                announcer.AnnounceImmediate($"NAV 1 course {deg:000}");
                return true;
            }

            case "DA40_RADIO_HDG_BUG_SET":
            {
                int deg = ((int)Math.Round(value) % 360 + 360) % 360;
                simConnect.ExecuteCalculatorCode($"{deg} (>K:HEADING_BUG_SET)");
                announcer.AnnounceImmediate($"Heading bug {deg:000}");
                return true;
            }
        }

        return false;
    }
}
