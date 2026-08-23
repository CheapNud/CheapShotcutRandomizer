using CheapHelpers.Services.DataExchange.Xml;
using CheapShotcutRandomizer.Core.Models;
using CheapShotcutRandomizer.Services;
using FluentAssertions;
using Moq;

namespace CheapShotcutRandomizer.Tests.Services;

/// <summary>
/// Tests for merging additional projects into the primary as source pools
/// </summary>
public class ShotcutServiceMergeTests
{
    private static Mlt BuildProject(string clipPrefix, int clips)
    {
        return new Mlt
        {
            Profile = new Profile { Width = "1920", Height = "1080", Frame_rate_num = "25", Frame_rate_den = "1" },
            Chain = [.. Enumerable.Range(0, clips).Select(i => new Chain
            {
                Id = $"chain{i}",
                Property = [new() { Name = "resource", Text = $"{clipPrefix}{i}.mp4" }]
            })],
            Playlist =
            [
                new Playlist { Id = "background" },
                new Playlist
                {
                    Id = "playlist1",
                    Entry = [.. Enumerable.Range(0, clips).Select(i => new Entry
                    {
                        Producer = $"chain{i}",
                        In = "00:00:00.000",
                        Out = "00:00:10.000"
                    })],
                    Property = [new() { Name = "shotcut:video", Text = "1" }, new() { Name = "shotcut:name", Text = "V1" }]
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
    public void Merge_Imports_Chains_And_Playlists_Without_Tracks()
    {
        var primary = BuildProject("main", 2);
        var extra = BuildProject("other", 3);
        var shotcutService = new ShotcutService(new Mock<IXmlService>().Object);

        shotcutService.MergeSourceProject(primary, extra, @"D:\somewhere\extra.mlt", "src1");

        // Chains imported with prefixed ids
        primary.Chain.Should().HaveCount(5);
        primary.Chain.Skip(2).Should().OnlyContain(c => c.Id.StartsWith("src1_chain"));

        // Timeline playlist imported, entries remapped, name suffixed with file stem
        primary.Playlist.Should().HaveCount(3);
        var merged = primary.Playlist[2];
        merged.Id.Should().Be("src1_playlist1");
        merged.Entry.Should().OnlyContain(e => e.Producer.StartsWith("src1_chain"));
        merged.Name.Should().Be("V1 (extra)");

        // Source pool only: no tractor track added
        primary.Tractor[0].Track.Should().HaveCount(2);
    }

    [Fact]
    public void Merge_Skips_Playlists_Not_On_The_Timeline()
    {
        var primary = BuildProject("main", 2);
        var extra = BuildProject("other", 2);
        // Bin-style playlist not referenced by any tractor track
        extra.Playlist.Add(new Playlist
        {
            Id = "main_bin",
            Entry = [new Entry { Producer = "chain0", In = "00:00:00.000", Out = "00:00:10.000" }]
        });
        var shotcutService = new ShotcutService(new Mock<IXmlService>().Object);

        shotcutService.MergeSourceProject(primary, extra, @"D:\somewhere\extra.mlt", "src1");

        primary.Playlist.Should().NotContain(p => p.Id.EndsWith("main_bin"));
    }

    [Fact]
    public void Merge_Absolutizes_Extra_Project_Resources()
    {
        var tempDir = Directory.CreateTempSubdirectory("randomizer-merge-test");
        try
        {
            var mediaPath = Path.Combine(tempDir.FullName, "clip.mp4");
            File.WriteAllText(mediaPath, "stub");

            var primary = BuildProject("main", 1);
            var extra = BuildProject("other", 1);
            extra.Chain[0].Property.First(p => p.Name == "resource").Text = "clip.mp4";

            var shotcutService = new ShotcutService(new Mock<IXmlService>().Object);
            shotcutService.MergeSourceProject(primary, extra, Path.Combine(tempDir.FullName, "extra.mlt"), "src1");

            primary.Chain[1].Property.First(p => p.Name == "resource").Text.Should().Be(mediaPath);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void IsMergedSourcePlaylist_Detects_Prefixed_Ids()
    {
        ShotcutService.IsMergedSourcePlaylist(new Playlist { Id = "src1_playlist1" }).Should().BeTrue();
        ShotcutService.IsMergedSourcePlaylist(new Playlist { Id = "src12_playlist3" }).Should().BeTrue();
        ShotcutService.IsMergedSourcePlaylist(new Playlist { Id = "playlist1" }).Should().BeFalse();
        ShotcutService.IsMergedSourcePlaylist(new Playlist { Id = "background" }).Should().BeFalse();
    }
}
