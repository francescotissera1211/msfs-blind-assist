---
paths:
  - "MSFSBlindAssist/Forms/TcasForm.cs"
  - "MSFSBlindAssist/Services/TcasService.cs"
---
# Ground-traffic rules the traffic window and the TCAS service carry

MIRRORS: copied word for word from ground-traffic.md, whose globs leave these files out; change both together (ClaudeContextBudgetTests checks).

- [TRF-16] A traffic aircraft is named through `GroundTrafficLogic.NamingType` and `SpokenAirline` alone: the type its title names, else the designator its package declares (`GroundTrafficModelCatalog.Declared`), said by `SpokenTypes` (else read as its code, never its maker); the airline its model gives, else the one installed liveries name. Full: docs/invariants/ground-traffic.md#trf-16
- [TRF-18] The one aircraft the pilot asked to be told about (`GroundTrafficMonitor.WatchUntilPassed`) speaks only through `EvaluateWatched` (`TrafficCalloutKind.Watched`, state committed in `OnEmitted`), never an Awareness or "on your route" line; "Clear of ...:" only once `GroundTrafficLogic.HasPassed`, never for one that has stopped. Full: docs/invariants/ground-traffic.md#trf-18
