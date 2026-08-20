using CheapHelpers.Services.DataExchange.Xml;
using CheapShotcutRandomizer.Core.Models;
using CheapShotcutRandomizer.Services;
using FluentAssertions;
using Moq;

namespace CheapShotcutRandomizer.Tests.Services;

/// <summary>
/// Tests for the grid compilation generation (independent and split modes)
/// </summary>
public class ShotcutServiceGridTests
{
    private static Mlt BuildProject(int sourceEntries, int entrySeconds)
    {
        var entries = Enumerable.Range(0, sourceEntries)
            .Select(i => new Entry
            {
                Producer = $"clip{i}",
                In = "00:00:00.000",
                Out = TimeSpan.FromSeconds(entrySeconds).ToString(@"hh\:mm\:ss\.fff")
            })
            .ToList();

        return new Mlt
        {
            Profile = new Profile { Width = "1920", Height = "1080", Frame_rate_num = "25", Frame_rate_den = "1" },
            Playlist =
            [
                new Playlist { Id = "background" },
                new Playlist
                {
                    Id = "playlist1",
                    Entry = entries,
                    Property = [new() { Name = "shotcut:name", Text = "source" }]
                }
            ],
            Tractor =
            [
                new Tractor
                {
                    Id = "tractor0",
                    Property = [new() { Name = "shotcut", Text = "1" }],
                    Track =
                    [
                        new Track { Producer = "background" },
                        new Track { Producer = "playlist1" }
                    ]
                }
            ]
        };
    }

    [Fact]
    public void SplitMode_Partitions_One_Compilation_Across_All_Cells()
    {
        var project = BuildProject(sourceEntries: 8, entrySeconds: 10);
        var shotcutService = new ShotcutService(new Mock<IXmlService>().Object);

        var trackIndices = shotcutService.GenerateGridCompilation(
            project,
            [(1, 0)],
            durationWeight: 0,
            numberOfVideosWeight: 1,
            cells: 4,
            splitSingleCompilation: true);

        trackIndices.Should().HaveCount(4);

        // Every cell playlist exists, carries a grid filter, and the union of all
        // cell entries equals one compilation (no duplication across cells)
        var cellPlaylists = project.Playlist.Skip(2).ToList();
        cellPlaylists.Should().HaveCount(4);
        cellPlaylists.Should().OnlyContain(p => p.Filter.Count == 1);

        var totalCellEntries = cellPlaylists.Sum(p => p.Entry.Count);
        var distinctProducers = cellPlaylists.SelectMany(p => p.Entry).Select(e => e.Producer).Distinct().Count();
        distinctProducers.Should().Be(totalCellEntries, "split mode must never duplicate a clip across cells");

        // Duration-balanced: no cell should hold more than half the total
        var totalDuration = cellPlaylists.Sum(p => p.Entry.Sum(e => e.Duration));
        cellPlaylists.Should().OnlyContain(p => p.Entry.Sum(e => e.Duration) <= totalDuration / 2);
    }

    [Fact]
    public void SplitMode_Lopsided_Durations_Leave_No_Empty_Cells()
    {
        var project = BuildProject(sourceEntries: 4, entrySeconds: 10);
        // Make the first clip dominate the total duration
        project.Playlist[1].Entry[0].Out = "00:10:00.000";
        var shotcutService = new ShotcutService(new Mock<IXmlService>().Object);

        shotcutService.GenerateGridCompilation(
            project, [(1, 0)], 0, 1, cells: 4, splitSingleCompilation: true);

        var cellPlaylists = project.Playlist.Skip(2).ToList();
        cellPlaylists.Should().HaveCount(4);
        cellPlaylists.Should().OnlyContain(p => p.Entry.Count > 0,
            "a single long clip must not vault the partition over cells");
    }

    [Fact]
    public void SplitMode_Fewer_Clips_Than_Cells_Skips_Unfillable_Cells()
    {
        var project = BuildProject(sourceEntries: 3, entrySeconds: 10);
        var shotcutService = new ShotcutService(new Mock<IXmlService>().Object);

        var trackIndices = shotcutService.GenerateGridCompilation(
            project, [(1, 0)], 0, 1, cells: 4, splitSingleCompilation: true);

        trackIndices.Should().HaveCountLessThanOrEqualTo(3);
        project.Playlist.Skip(2).Should().OnlyContain(p => p.Entry.Count > 0,
            "no empty cell playlists are ever added");
    }

    [Fact]
    public void IndependentMode_Generates_A_Compilation_Per_Cell()
    {
        var project = BuildProject(sourceEntries: 8, entrySeconds: 10);
        var shotcutService = new ShotcutService(new Mock<IXmlService>().Object);

        var trackIndices = shotcutService.GenerateGridCompilation(
            project,
            [(1, 0)],
            durationWeight: 0,
            numberOfVideosWeight: 1,
            cells: 2,
            splitSingleCompilation: false);

        trackIndices.Should().HaveCount(2);

        var cellPlaylists = project.Playlist.Skip(2).ToList();
        cellPlaylists.Should().HaveCount(2);
        cellPlaylists.Should().OnlyContain(p => p.Entry.Count > 0, "each cell gets its own full compilation");

        // Transitions planted for every cell track
        var tractor = project.Tractor[0];
        foreach (var trackIndex in trackIndices)
        {
            tractor.Transition.Should().Contain(t =>
                t.Property.Any(pr => pr.Name == "mlt_service" && pr.Text == "qtblend") &&
                t.Property.Any(pr => pr.Name == "b_track" && pr.Text == trackIndex.ToString()));
        }
    }
}
