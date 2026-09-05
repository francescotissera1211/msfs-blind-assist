# DA40-XLS — the variable surface

Companion to [da40.md](da40.md), which covers the NG. This is the **name** half of the XLS
probe pass: what exists, how it groups, and which variables the six unbuilt panels will need.
It is deliberately NOT a claim about values, units or ranges — see **What this does not tell
you**.

## Method

Names come from the package's own model XML, which is the complete and authoritative list —
not from a live enumeration, which cannot see them (next section).

```bash
PKG="$LOCALAPPDATA/Packages/Microsoft.FlightSimulator_8wekyb3d8bbwe/LocalCache/Packages/Community/cows-da40/SimObjects/Airplanes"
ex(){ grep -rhoE 'L:[A-Za-z0-9_]+(:[A-Za-z0-9]+)?' "$1" --include=*.xml | sed 's/^L://' | sort -u; }
ex "$PKG/COWS_DA40XLS" > xls.txt; ex "$PKG/COWS_DA40NG" > ng.txt
sed -E 's/:[A-Za-z0-9]+$//' xls.txt | sort -u > xlsb.txt
sed -E 's/:[A-Za-z0-9]+$//' ng.txt  | sort -u > ngb.txt
comm -23 xlsb.txt ngb.txt          # the 379 XLS-only base names
```

⚠️ **Do not narrow that pattern to require a leading `(`.** An earlier pass matched only
`(L:NAME)` and silently lost 19 variables the XML references another way — including
`ASSIST_PRIME_PERCENT`, `DISP_MAP`, and the four `DISP_LEAN_*` lean-assist readouts that panel
7 is entirely about. A dropped name looks exactly like a variable that does not exist.

| | Full names | Base names (`:n` collapsed) |
|---|---|---|
| XLS total | 1398 | 998 |
| NG total | 1075 | 980 |
| Shared | 662 | 619 |
| **XLS-only** | **736** | **379** |
| NG-only | 413 | 361 |

Quote the 379; the 736 counts `DAMAGE_MAG_FOUL:1L` and `:1R` as two variables. It agrees with
the 374 in the working plan to within counting method.

⚠️ **These count the PACKAGE, not MSFSBA's profile.** [da40.md](da40.md)'s "86 are NG-only"
counts `CowsDA40Definition`'s own variable definitions — a different measurement of a different
thing. Do not reconcile the two.

## ⚠️ The live enumerator cannot list these

`msfs_list_lvars` (MobiFlight `MF.LVars.List`) **caps at 1000 names, alphabetically, and still
reports the list as complete.** With other add-ons loaded that cap is spent long before the
XLS's engine model: a measured run returned A32NX, A380X, AS3000, AS3X, Aera, ASVigilus, B789,
DHC2 and FA18 variables and ran out at `FAILURES_DISP_EGT`. Everything after that — `FUEL_*`,
`MIXTURE_*`, `OC_*`, `PROP_*`, `STARTER_*`, `TB_*`, the whole induction and oil-cooler model —
is invisible, and `filter_prefix` does not help: it filters the already-truncated response
client-side, so `filter_prefix=STARTER` returns zero while `L:STARTER_SWITCH` exists and reads
fine.

**Enumerate from the package XML; read values with `msfs_get_lvar` or the calculator path,**
which work for any name whether or not it was listed. Never conclude a variable is absent from
a listing that came back truncated.

## What this does not tell you

**No value here was measured.** The flight model was not running during this pass
(`sim_running: false`, with `GENERAL ENG STARTER ACTIVE`, `RECIP ENG STARTER TORQUE`,
`ELECTRICAL BATTERY LOAD`, `GENERAL ENG RPM` and `ENG COMBUSTION` all at exactly 0 — the
ready-to-fly/menu-screen signature documented in [da40.md](da40.md)). Units, ranges, encodings
and polarity are all open, and the NG's answers do not transfer: `FILTER_RESRTICTION` and
`HEALTH_BLOCK/800` are both cases where a plausible reading of a name was not a reading of the
variable.

## The five XLS interaction components

Everything clickable the NG does not also have, from `<Component ID=…>` in `COWS_DA40_IN.xml`
and `COWS_DA40_Inputs.xml`:

`ENGINE_Lever_Mixture_1`  `ENGINE_Lever_Propeller_1`  `ENGINE_pedestal`  `FUEL_SELECTOR`  `STARTER`

## The controls behind the six unbuilt panels

| Panel | Reads / writes | Notes |
|---|---|---|
| Magnetos | `STARTER_SWITCH` (5-state), `A:RECIP ENG LEFT/RIGHT MAGNETO:1`, `ENG_MAG_PWR:L`/`:R`, `FAILURES_MAG_L/R`, `FAILURES_MAG_GND_L/R` | the key and the starter are ONE control — below |
| Power and Levers | `THROTTLE_LEVER`, `INPUT_PROPELLER`, `INPUT_MIXTURE`; mirrors `STATE_THROTTLE_SPREAD`, `STATE_PROP_LVR`, `STATE_MIXT_LVR` | manifold pressure is `TB_*` — below |
| Fuel System | `FUEL_SELECTOR`, `STATE_FUEL_SELECTOR`, `ENG_FUEL_LINE_PRIMED:1..4` + `:S`, `ENG_FUEL_SYSTEM_PRIMED`, `ENG_FUEL_LINE_FLOW_CHECK`, `FUEL_TEMP_BOIL*` | vapour lock is a whole family, not one flag |
| Mixture and Propeller | `MIXTURE_VALVE`, `MIXTURE_SET_BEST`, `MIXTURE_SET_AVG`, `EGT_MIXTURE`, `CYL_SPREAD_EGT`, `DISP_LEAN_PEAK:1..4`, `DISP_LEAN_DELTA`, `DISP_LEAN_DELTA_BIGGEST`, `DISP_LEAN_HOTEST`, `DISP_LEAN_HIGHLIGHT`, `OP_PROP_TARGET_RPM`, `OP_PROP_BETA` | the panel the XLS exists for |
| Priming | `ASSIST_PRIME_CYL_GRAM` vs `ASSIST_PRIME_CYL_REQ`, `ASSIST_PRIME_SYS_GRAM`, `ASSIST_PRIME_PERCENT`, `ASSIST_PRIME_ACTIVE` | grams against a requirement — say both, never a bare percent |
| Engine Start | `STARTER_SWITCH`, `STARTER_SPAD:1`, `STARTER_HOLD`, `STARTER_AMPS`, `STARTER_POWER:1`, `START_HOT`, `START_MIXTURE`, `RESET_FLOOD` | `START_HOT` is the hot start the AFM gives its own procedure |

### ⚠️ The magneto key and the starter are the same control

`<Component ID="STARTER">` is an `ASOBO_GT_Switch_5States` whose `SWITCH_POSITION_VAR` is
**`L:STARTER_SWITCH`** — one 5-position key, OFF / R / L / BOTH / START, carrying
`<MOMENTARY_SWITCH/>` with `STATE_MAX_TIMER 1` so the START detent springs back. Position 4
alone fires `1 (>K:SET_STARTER1_HELD)`; positions 0-3 send `0`. So the plan's task 2 and task 5
share one combo, and cranking is a detent rather than the NG's separate start button.

⚠️ **The NG's starter findings must not be assumed here.** On the NG, [da40.md](da40.md)
records `L:STARTER_SWITCH` as a READ-ONLY MIRROR and `K:SET_STARTER1_HELD` as INERT, with
`L:STARTER_SPAD:1` the real input. On the XLS the template makes `STARTER_SWITCH` the switch's
own position variable and the model uses `SET_STARTER1_HELD` directly. That is a reading of the
XML, **not a measurement** — write it and read it back live before anything depends on it.
`STARTER_SPAD:1` exists on the XLS too, so if the 5-state write is refused, that is the first
fallback to try.

### Manifold pressure exists, under `TB_*`

The number every Lycoming procedure is written in lives in the throttle-body family:
`TB_TARGET_MAP`, `TB_SET_MAP`, `TB_CALC_MAP`, with `FILTER_TARGET_MAP` ahead of them, `DISP_MAP`
and `DISP_MAP_PROBE` for what the G1000 draws, and `FAILURES_DISP_MAP` for a failed gauge. Per
[da40.md](da40.md)'s rule, take the value from the model variable, never from a `DISP_*`, which
is what the display draws rather than what the sensor measured.

### Two reset buttons the plan does not mention

`RESET_FLOOD` and `RESET_PLUGS` sit beside the NG's `RESET_DAMAGE`. A flooded engine and fouled
plugs are the two classic hot- and rough-start failures on an injected Lycoming, and both are
recoverable in the aeroplane — so both belong on Engine Start as actions, with the condition
that produced them announced first.

### Also worth knowing

- **XLS-specific breakers.** `CB_ACN`, `CB_ALT`, `CB_ALT_OVER`, `CB_APT`, `CB_BATT_OVER`,
  `CB_FAN`, `CB_FUP`, `CB_MAIN` (+ their `STATE_CB_*` mirrors) are not among the NG's 34, so the
  breaker panel is not variant-agnostic after all and its list needs rebuilding per variant.
- **`FAILURES_MAG_GND_L`/`_R`** is a grounded P-lead — the failure a live mag check exists to
  find, and the reason the run-up drop must be readable as a number (`ENG_MAG_PWR:L`/`:R`).
- **The vendor misspells "restriction" consistently.** The NG's `FILTER_RESRTICTION` has an XLS
  sibling in `TB_RSRTIC` / `TB_RSRTIC_100000`. Grep for `RSRTIC` as well as `RESTRIC`.
- **`BRO_CMON_PLEASE_STOP` and `SUPER_SECRET_ENGINE_DEBUG`** are developer variables, not
  controls. Kept in the appendix because a dump that quietly drops what it cannot explain is not
  a dump.

## Appendix — the 379 XLS-only variables, by family

Base names, index suffixes such as `:1` / `:1L` collapsed. Generated from the package XML
by the command in **Method**; `4` is an extraction artefact of that regex, not a variable.

### Assistance layer (7)

`AUTOMIXTURE AUTOMIXTURE_FORCE AUTOMIXTURE_TARGET AUTOMIXTURE_TARGET_FF AUTOSTART_MIX AUTOSTART_STARTER_TIMER AUTOSTART_THROT`

### Combustion (ENG_COMP_*) (19)

`ENG_COMP_AIRMASS_CYL ENG_COMP_AIRTEMP_CYL ENG_COMP_COMPRESSION_CYL ENG_COMP_CRANK_CYL ENG_COMP_FORCE_CYL ENG_COMP_FRIC_OIL ENG_COMP_IGN_CYL ENG_COMP_IGN_CYL_SOUND ENG_COMP_MAG_CYL ENG_COMP_POS_360 ENG_COMP_POS_720_2 ENG_COMP_POS_CYL ENG_COMP_PRESS_CYL ENG_COMP_PROP_DRAG ENG_COMP_PROP_WIND ENG_COMP_PROP_WIND_FAC ENG_COMP_PWR ENG_COMP_SHAKE ENG_COMP_STROKE_CYL`

### Detonation (4)

`DETONATION DETONATION_INT_TEMP_C DETONATION_TEMP_C DETONATION_TEMP_FAC`

### Engine efficiency + friction model (16)

`COMBUSTION COOKING ENG ENG_FRIC ENG_FRIC_FAC ENG_FRIC_HI ENG_FRIC_LO ENG_FRIC_RPM_HI ENG_FRIC_RPM_LO ENG_MECH_EFF ENG_MECH_EFF_FAC ENG_MECH_EFF_HI ENG_MECH_EFF_LO ENG_MECH_EFF_RPM_HI ENG_MECH_EFF_RPM_LO ENG_OIL_PWR_FAC`

### Exhaust temp, spread, lean assist (36)

`CYL_SPREAD_COOL CYL_SPREAD_EGT CYL_SPREAD_INJ CYL_SPREAD_SET DISP_LEAN_ASSIST DISP_LEAN_DELTA DISP_LEAN_DELTA_BIGGEST DISP_LEAN_HIGHLIGHT DISP_LEAN_HOTEST DISP_LEAN_PEAK EGT EGT_DELTA EGT_MANI EGT_MANI_COOLING EGT_MANI_HEATING EGT_MAP_FACTOR EGT_MAX EGT_MIXTURE EGT_PROBE EGT_RPM_FACTOR EGT_TABLE_MIX_HI EGT_TABLE_MIX_LO EGT_TABLE_MIX_RNG EGT_TABLE_TEMP_FAC EGT_TABLE_TEMP_HI EGT_TABLE_TEMP_LO EGT_TABLE_TEMP_RNG EGT_TARGET ENG_EGT ENG_EGT_FAC ENG_EGT_FAC_HI ENG_EGT_FAC_LO ENG_EGT_MIX_HI ENG_EGT_MIX_LO ENG_MAG_EGT SOUND_LEANEST_MIXTURE`

### Fuel line, temperature + vapour lock (34)

`ENG_FUEL_FIREWALL_TEMP_C ENG_FUEL_FIREWALL_TEMP_COOLING ENG_FUEL_FIREWALL_TEMP_HEATING ENG_FUEL_FLOW_KG ENG_FUEL_LINE_BOIL ENG_FUEL_LINE_FLOW ENG_FUEL_LINE_FLOW_CHECK ENG_FUEL_LINE_FLOW_FAC ENG_FUEL_LINE_GRAM ENG_FUEL_LINE_IN_G ENG_FUEL_LINE_OUT_G ENG_FUEL_LINE_PRIMED ENG_FUEL_LINE_TEMP ENG_FUEL_LINE_TEMP_COOLING_AIR ENG_FUEL_LINE_TEMP_COOLING_FUEL ENG_FUEL_LINE_TEMP_HEATING ENG_FUEL_OUTSIDE_CYL_GRAM ENG_FUEL_PRESS ENG_FUEL_SYSTEM_FLOW_ENG_G ENG_FUEL_SYSTEM_FLOW_EXCES ENG_FUEL_SYSTEM_FLOW_EXCES_PRESSURE ENG_FUEL_SYSTEM_FLOW_G ENG_FUEL_SYSTEM_FLOW_TO_SERVO_G ENG_FUEL_SYSTEM_GRAM ENG_FUEL_SYSTEM_PRIMED ENG_FUEL_SYSTEM_SERVO_GRAM FUEL_QUANT_SLOSH FUEL_TEMP FUEL_TEMP_BOIL FUEL_TEMP_BOIL_FAC FUEL_TEMP_BOIL_FAC_FF FUEL_TEMP_BOIL_PRESS FUEL_TEMP_PUMP_PRESS_DROP STATE_FUEL_SYSTEM`

### Fuel selector + fuel spread (3)

`FSC_FUEL_SPREAD_PRESSURE FUEL_SPREAD_PRESSURE STATE_FUEL_SPREAD_PRESSURE`

### G1000 display probes (DISP_*) (17)

`DISP_CHT DISP_CHT_HOT DISP_CHT_HOT_CYL DISP_EGT DISP_FF_PROBE DISP_FP DISP_FP_PROBE DISP_MAP DISP_MAP_PROBE DISP_OP_PROBE DISP_PROP_RPM_PROBE FAILURES_DISP_CHT FAILURES_DISP_EGT FAILURES_DISP_FF FAILURES_DISP_FP FAILURES_DISP_MAP FAILURES_DISP_RPM`

### Head temp + block thermal (CHT_*, BC_*) (40)

`BC_COOLING BC_COOLING_AIR BC_COOLING_OIL BC_HEATING BC_HEATING_CHT BC_HEATING_FRIC BC_TEMPERATURE BC_TEMP_DIFF BC_TEMP_DIFF_OIL BC_TEMP_INC CHT_AIRFLOW CHT_AIRFLOW_ANG CHT_AIRFLOW_ANG_FAC CHT_AIRFLOW_IND CHT_AIRFLOW_INDUCED CHT_AIRFLOW_TOTAL CHT_AIRFLOW_WIND CHT_AIRF_AX_KNOTS CHT_AIRF_AX_KNOTS_DIFF CHT_AIRF_BETA CHT_AIRF_RAD_KNOTS CHT_C CHT_COOLING CHT_COOLING_OIL CHT_EFF_F CHT_EFF_R CHT_EGT_DELTA_K CHT_F CHT_HEATING_KW CHT_HEAT_EXHT_AIR_KW CHT_HEAT_EXHT_FUEL_KW CHT_HEAT_EXHT_KW CHT_HEAT_FRICTION_KW CHT_HEAT_FUEL_FLOW_KW CHT_HEAT_OUTPUT_KW CHT_PROBE CHT_TEMP_DIFF CHT_TEMP_INC FAILURES_CHT_BAFFLE FAILURES_CHT_OIL`

### Induction + manifold pressure (TB_*) (43)

`DENSITY_CORRECTION ENG_INT_MANI ENG_INT_MANI_COOLING ENG_INT_MANI_EVAP ENG_INT_MANI_HEATING FILTER_TARGET_MAP INT_AIR_DENSITY INT_AIR_DENSITY_TEMP_FAC TB_CALC_MAP TB_FF_FUEL_FLOW_KG TB_FF_JET_PRESS_G TB_FF_JET_PRESS_KG TB_FF_JET_THROTTLE_KG TB_FF_JET_VENTURI_KG TB_FF_MIXTURE TB_FF_MIXTURE_RATIO TB_FF_SERVO TB_FF_VENTURI_P_LOSS_FAC TB_FF_VENTURI_P_LOSS_PASC TB_FF_VENTURI_SPEED_M TB_FUEL_FLOW_G TB_FUEL_FLOW_GPH TB_INT_AIR_DENSITY_TEMP_FAC TB_INT_PRSS TB_INT_TEMP_C TB_INT_TEMP_F TB_INT_TEMP_K TB_MASS_FLOW_KG TB_POS TB_PRSS_RED TB_PUMP_LOSS_PWR TB_RSRTIC TB_RSRTIC_100000 TB_SET_FIX TB_SET_MAP TB_TARGET_MAP TB_VOL_EFF TB_VOL_EFF_FAC TB_VOL_EFF_HI TB_VOL_EFF_LO TB_VOL_EFF_RPM_HI TB_VOL_EFF_RPM_LO TB_VOL_FLOW_M`

### Levers + prop governor (30)

`FAILURES_MIX_LEVER FAILURES_PROP_LEVER FAILURES_PROP_PUMP FAILURES_THROT_LEVER FSC_PROP_SPREAD_HI FSC_PROP_SPREAD_LO FSC_THROTTLE_SPREAD INPUT_MIXTURE INPUT_PROPELLER MIXTURE_LEVER_GAME_SET MIXTURE_SET_AVG MIXTURE_SET_BEST MIXTURE_VALVE MIXTURE_VALVE_RST OP_PROP_BETA OP_PROP_OIL_LOSS OP_PROP_OIL_PRIME OP_PROP_TARGET_RPM OP_PROP_TEMP_FAC PROP_ANI PROP_SPREAD_HI PROP_SPREAD_LO STATE_MIXT_LVR STATE_PROP_LVR STATE_PROP_SPREAD_HI STATE_PROP_SPREAD_LO STATE_THROTTLE_SPREAD THROTTLE_LEVER THROTTLE_LEVER_LINK THROTTLE_SPREAD`

### Magnetos + plug fouling (16)

`DAMAGE_MAG_FOUL DAMAGE_MAG_FOUL_RATE ENG_MAG_CYL ENG_MAG_FOUL_PWR ENG_MAG_PWR ENG_MAG_PWR_REQ ENG_MAG_PWR_REQ_DENS FAILURES_MAG FAILURES_MAG_GND_L FAILURES_MAG_GND_R FAILURES_MAG_L FAILURES_MAG_R FSC_MAG_SPREAD_TIMING MAG_SPREAD_TIMING RESET_PLUGS STATE_MAG_SPREAD_TIMING`

### Oil cooler + oil thermal (OC_*) (21)

`FAILURES_THERMOSTAT_OIL OC_COOLING OC_COOLING_ACT OC_COOLING_PAS OC_EFF OC_HEATING OC_HEATING_BLOCK OC_HEATING_CHT OC_HEATING_FRIC OC_HEATING_FUEL OC_OIL_FLOW_CHT OC_PUMP_SPEED OC_TEMPERATURE OC_TEMPERATURE_F OC_TEMP_DIFF OC_TEMP_DIFF_CHT OC_TEMP_INC OC_THERMOSTAT OC_THERMOSTAT_T OP_OIL_PRIME OT_PROBE`

### Other XLS-specific failures (7)

`FAILURES_FUEL_INJ FAILURES_FUEL_LEAK FAILURES_FUEL_LEAK_L FAILURES_FUEL_LEAK_R FAILURES_FUEL_PUMP FAILURES_FUEL_SPRING FAILURES_VACC_LEAK`

### Per-cylinder health + damage (14)

`CYL_SHAKE DAMAGE_BLOCK_FAC DAMAGE_CYL DAMAGE_CYL_FAC ENG_ATOMISE_CYL ENG_CYL_POWER_HEALTH_FAC ENG_CYL_PWR_FAC ENG_CYL_SHAKE_FAC FAILURES_CYL HEALTH_CYL KAPUTT_CYL RUN_COMB_CYL SOUND_CUT_CYL WORKING_CYL`

### Per-cylinder spread / build variation (18)

`FSC_CYL_SPREAD_COOL FSC_CYL_SPREAD_EGT FSC_CYL_SPREAD_INJ FSC_ENG_COMP_RPM FSC_OP_SPREAD_BYPASS FSC_SPREAD_OC FSC_SPREAD_OP FSC_SPREAD_ROUGH SPREAD_INJ_TRIM SPREAD_OC SPREAD_OP SPREAD_ROUGH STATE_CYL_SPREAD_COOL STATE_CYL_SPREAD_EGT STATE_CYL_SPREAD_INJ STATE_SPREAD_OC STATE_SPREAD_OP STATE_SPREAD_ROUGH`

### Priming (ASSIST_PRIME_*) (6)

`ASSIST_PRIME ASSIST_PRIME_ACTIVE ASSIST_PRIME_CYL_GRAM ASSIST_PRIME_CYL_REQ ASSIST_PRIME_PERCENT ASSIST_PRIME_SYS_GRAM`

### Red box (mixture damage) (4)

`DAMAGE_REDBOX_FAC DAMAGE_REDBOX_ITS DAMAGE_REDBOX_LOP DAMAGE_REDBOX_ROP`

### Sensors + filters (8)

`FILTER_CHT_TEMP_INC FILTER_PRSS_RED RAND_FP RPM_SENS_POS RPM_SENS_RESET RPM_SENS_RPM RPM_SENS_TIME RPM_SENS_TIME_FIX`

### Sound model (2)

`SOUND_ENGINE_HIGHS SOUND_TORQUE`

### Starter, mag key + flood reset (4)

`ENG_START_AIRVOL_CYL RESET_FLOOD START_MIXTURE START_MIXTURE_START`

### Unclassified (4)

`4 BRO_CMON_PLEASE_STOP STATE_FLAPS_R SUPER_SECRET_ENGINE_DEBUG`

### XLS-specific breakers + electrics (26)

`CB_ACN CB_ALT CB_ALT_OVER CB_APT CB_BATT_OVER CB_FAN CB_FUP CB_MAIN ELEC_ALT_ACTUAL_AMPS ELEC_BUS_ESS ELEC_ESS FAILURES_ALT_OVERVOLT FAILURES_CB_ADF FAILURES_CB_ALT_CONT FAILURES_CB_ALT_PROT FAILURES_CB_AV_BUS FAILURES_CB_BATT FAILURES_CB_FUEL_PUMP STATE_ALTERNATOR STATE_CB_ACN STATE_CB_ALT STATE_CB_APT STATE_CB_BAT STATE_CB_FAN STATE_CB_FUP STATE_CB_TAS`

