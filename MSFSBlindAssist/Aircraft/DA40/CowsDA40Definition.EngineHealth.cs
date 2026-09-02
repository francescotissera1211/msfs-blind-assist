using System;
using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// How healthy the engine is, which MSFSBA could not say at all.
///
/// ⚠️ THIS AEROPLANE ACCUMULATES ENGINE DAMAGE AND IT SURVIVES A RELOAD. The COWS damage
/// model tracks the block, the oil system, the turbocharger and the fuel system separately -
/// `DAMAGE_BLOCK`, `DAMAGE_OIL`, `DAMAGE_TURBO` with its own friction and heat terms,
/// `DAMAGE_FUEL` with cut and oscillation terms, plus dust ingestion and overstress - and
/// publishes the result as `HEALTH_*` fractions from 1.0 down. MSFSBA exposed the SWITCH that
/// turns damage modelling on and the BUTTON that resets it, and nothing whatsoever about the
/// state in between.
///
/// So a pilot could mishandle the engine, land, reload, and take off again in a damaged
/// aeroplane with no way to find out - which is precisely the trap the state-saving system
/// already sprang once on this project with flat batteries.
///
/// WHY IT IS A PERCENTAGE. The model works in fractions where 1.0 is a factory engine; a
/// pilot thinks in percent, and "block 100 percent" is a number anybody can act on where
/// "1.0" is a number you have to be told the scale of.
///
/// WHY A DROP IS ANNOUNCED. Damage happens DURING something - a cold-power application, an
/// overspeed, running the oil hot - and the moment it starts is the moment a pilot can still
/// do something about it. Waiting for them to open a panel is waiting for the flight to be
/// over. Only a material fall speaks, and only downwards: health climbing back means the
/// pilot pressed Reset, and the Reset button confirms itself.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>How far health must fall, in percent, before it is said again.</summary>
    private const double HealthStep = 5;

    private static readonly Dictionary<string, string> HealthKeys = new(StringComparer.Ordinal)
    {
        ["DA40_HEALTH_BLOCK"] = "Engine block",
        ["DA40_HEALTH_OIL"] = "Oil system",
        ["DA40_HEALTH_FUEL"] = "Fuel system"
    };

    /// <summary>Exposed for the tests, which check each one exists and is polled.</summary>
    public static IReadOnlyCollection<string> HealthKeyNames => HealthKeys.Keys;

    internal static void AddEngineHealth(Dictionary<string, SimVarDefinition> v)
    {
        Add("DA40_HEALTH_BLOCK", "HEALTH_BLOCK:1", "Engine Block Health");
        Add("DA40_HEALTH_OIL", "HEALTH_OIL:1", "Oil System Health");

        // ⚠️ HEALTH_FUEL has three indices - 1, 11 and 12. Only :1 is exposed, because what
        // the other two track is not written down anywhere in the package and guessing at a
        // label for a number a pilot might act on is worse than leaving it out. If they turn
        // out to be the two tanks, they are one line each to add.
        Add("DA40_HEALTH_FUEL", "HEALTH_FUEL:1", "Fuel System Health");

        void Add(string key, string lvar, string label)
        {
            v[key] = new SimVarDefinition
            {
                Name = lvar,
                DisplayName = label,
                Type = SimVarType.LVar,
                Units = "percent",
                // The model publishes 0 to 1; a pilot thinks in percent.
                Scale = 100,
                Format = "F0",
                UpdateFrequency = UpdateFrequency.Continuous,
                // Announced only to reach the batch - the falling-health announcer below
                // speaks instead, because health sitting at 100 percent is not news and a
                // percentage that drifts would otherwise chatter.
                IsAnnounced = true,
                ExcludeFromMonitorManager = false,
                HelpText = "100 is a factory engine. Damage survives a reload; Reset clears it."
            };
        }
    }

    private readonly Dictionary<string, double> _healthSpoken = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns true for a health reading, keeping it off the generic path: the generic
    /// announcer would read a percentage that moves, several times a second.
    /// </summary>
    private bool NoteEngineHealth(string varKey, double value, ScreenReaderAnnouncer announcer)
    {
        if (!HealthKeys.TryGetValue(varKey, out string? what)) return false;

        // The value arriving here is the RAW fraction; Scale is applied for display, not
        // for ProcessSimVarUpdate, so it is turned into a percentage here too.
        double percent = value <= 1.5 ? value * 100 : value;

        if (!_healthSpoken.ContainsKey(varKey))
        {
            _healthSpoken[varKey] = percent;
            return true;
        }

        double last = _healthSpoken[varKey];

        // Upwards is a Reset, and the Reset button already says so.
        if (percent >= last - 0.01)
        {
            if (percent > last) _healthSpoken[varKey] = percent;
            return true;
        }

        if (percent > last - HealthStep) return true;
        _healthSpoken[varKey] = percent;

        if (Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet.Contains(varKey))
        {
            return true;
        }

        announcer.AnnounceImmediate($"{what} health {percent:0} percent");
        return true;
    }
}
