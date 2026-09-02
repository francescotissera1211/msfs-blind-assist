using System;
using System.Linq;
using System.Xml.Linq;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Reading the aeroplane's own checklist out of its package.
///
/// The parsing is tested against a document built here rather than against the installed
/// aircraft, so it runs on any machine; the one test that does touch the real package skips
/// itself when it is not there.
/// </summary>
public class NativeChecklistReaderTests
{
    private static XDocument Sample() => XDocument.Parse("""
        <Checklist>
          <Step ChecklistStepId="PREFLIGHT_GATE">
            <Page SubjectTT="Before engine start">
              <Checkpoint>
                <CheckpointDesc SubjectTT="Parking Brake" ExpectationTT="Set"/>
                <Clue Name="Forwards = go, Backwards = stop"/>
              </Checkpoint>
              <Checkpoint>
                <CheckpointDesc SubjectTT="Alternate air" ExpectationTT="Closed"/>
              </Checkpoint>
            </Page>
          </Step>
          <Step ChecklistStepId="POSTFLIGHT">
            <Page SubjectTT="Tips and help">
              <Checkpoint>
                <CheckpointDesc SubjectTT="Warmup" ExpectationTT="Clue"/>
                <Clue Name="Idle for 2 min. Up to 50% load."/>
              </Checkpoint>
            </Page>
            <Page SubjectTT="Before engine start">
              <Checkpoint>
                <CheckpointDesc SubjectTT="Second page, same name" ExpectationTT="Noted"/>
              </Checkpoint>
            </Page>
          </Step>
        </Checklist>
        """);

    [Fact]
    public void APageBecomesACategoryAndACheckpointBecomesALine()
    {
        string text = NativeChecklistReader.RenderFile(Sample());

        Assert.Contains("[Before engine start]", text, StringComparison.Ordinal);
        Assert.Contains("Parking Brake ... Set", text, StringComparison.Ordinal);
        Assert.Contains("Alternate air ... Closed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACluesTextIsKeptBecauseItIsOftenTheOnlyPlaceADetailIsWritten()
    {
        string text = NativeChecklistReader.RenderFile(Sample());

        Assert.Contains("Forwards = go, Backwards = stop", text, StringComparison.Ordinal);
        Assert.Contains("Idle for 2 min. Up to 50% load.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ANoteDoesNotReadAsAnExpectationOfTheWordClue()
    {
        // Asobo puts the literal word "Clue" in the expectation column of a note row.
        // Rendering it would give "Warmup ... Clue", which means nothing to anybody.
        string text = NativeChecklistReader.RenderFile(Sample());

        Assert.DoesNotContain("... Clue", text, StringComparison.Ordinal);
        Assert.Contains("Warmup", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoPagesWithOneNameBothSurvive()
    {
        // The category dictionary downstream is keyed by NAME, so a duplicate would
        // silently swallow the first page's contents.
        string text = NativeChecklistReader.RenderFile(Sample());

        Assert.Contains("[Before engine start]", text, StringComparison.Ordinal);
        Assert.Contains("[Before engine start 2]", text, StringComparison.Ordinal);
        Assert.Contains("Second page, same name", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRealDA40ChecklistCarriesItsTipsPage()
    {
        // Skips itself when the aeroplane is not installed — a test that fails for being on
        // the wrong computer teaches people to ignore failures.
        string? text = NativeChecklistReader.Render("COWS_DA40NG");
        if (text is null) return;

        Assert.Contains("[Tips and help]", text, StringComparison.Ordinal);

        // The lines this whole exercise was for: the aeroplane's own operating knowledge.
        Assert.Contains("Idle for 2 min", text, StringComparison.Ordinal);
        Assert.Contains("Downwind", text, StringComparison.Ordinal);
        Assert.Contains("ECU test button for 10 seconds", text, StringComparison.Ordinal);

        // And the procedures themselves, not just the tips.
        Assert.Contains("[Before engine start]", text, StringComparison.Ordinal);
        Assert.True(text.Split('\n').Length > 100, "the whole checklist should be far longer");
    }

    [Fact]
    public void AnAircraftWithNoChecklistAnswersNullRatherThanGuessing()
    {
        Assert.Null(NativeChecklistReader.Render("NO_SUCH_AEROPLANE_12345"));
    }
}
