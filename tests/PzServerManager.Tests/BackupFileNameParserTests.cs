using PzServerManager.Application.Backups;

namespace PzServerManager.Tests;

public sealed class BackupFileNameParserTests
{
    private readonly BackupFileNameParser _parser = new("server.test+[1]");

    [Theory]
    [InlineData("server.test+[1]-20261003-142530-manual.tar.gz", BackupReason.Manual, "Manual", 14)]
    [InlineData("server.test+[1]-20261003-050000-diario.tar.gz", BackupReason.Daily, "Diário", 5)]
    [InlineData("server.test+[1]-20261003-142530-antes-restaurar.tar.gz", BackupReason.BeforeRestore, "Antes de restaurar", 14)]
    [InlineData("server.test+[1]-20261003-142530-antes-zerar.tar.gz", BackupReason.BeforeReset, "Antes de zerar", 14)]
    [InlineData("server.test+[1]-20261003-142530-atualizacao-mods.tar.gz", BackupReason.UpdateMods, "Atualização de mods", 14)]
    [InlineData("server.test+[1]-20261003-142530-atualizacao-jogo.tar.gz", BackupReason.UpdateGame, "Atualização do jogo", 14)]
    public void TryParse_AcceptsKnownReasons(string fileName, BackupReason reason, string label, int expectedHour)
    {
        Assert.True(_parser.TryParse(fileName, 123, out var backup));
        Assert.Equal(reason, backup!.Reason);
        Assert.Equal(label, backup.ReasonLabel);
        Assert.Equal(123, backup.SizeBytes);
        Assert.Equal(2026, backup.Timestamp.Year);
        Assert.Equal(10, backup.Timestamp.Month);
        Assert.Equal(3, backup.Timestamp.Day);
        Assert.Equal(expectedHour, backup.Timestamp.Hour);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../server.test+[1]-20261003-142530-manual.tar.gz")]
    [InlineData("server.test+[1]-20261303-142530-manual.tar.gz")]
    [InlineData("server.test+[1]-20261003-256000-manual.tar.gz")]
    [InlineData("server.test+[1]-20261003-142530-outro.tar.gz")]
    [InlineData("server.test+[1]-20261003-142530-MANUAL.tar.gz")]
    [InlineData("server.test+[1]-20261003-142530-manual.tar.gz.extra")]
    [InlineData("outro-20261003-142530-manual.tar.gz")]
    [InlineData("serverXtest+[1]-20261003-142530-manual.tar.gz")]
    [InlineData("server.test+11-20261003-142530-manual.tar.gz")]
    [InlineData("prefix-server.test+[1]-20261003-142530-manual.tar.gz")]
    [InlineData("SERVER.TEST+[1]-20261003-142530-manual.tar.gz")]
    [InlineData("server.test+[1] -20261003-142530-manual.tar.gz")]
    [InlineData("server.test+[1]-20261003-142530-manual.zip")]
    [InlineData("server.test+[1]-20261003-142530-manual.tar")]
    [InlineData("server.test+[1]-20261003-142530--manual.tar.gz")]
    [InlineData("server.test+[1]-20261003-142530-manual!.tar.gz")]
    [InlineData("server.test+[1]-20261003-142530-manual.tar.GZ")]
    public void TryParse_RejectsInvalidNames(string fileName) =>
        Assert.False(_parser.TryParse(fileName, 0, out _));

    [Fact]
    public void ParseAndSort_IgnoresInvalidAndReturnsNewestFirst()
    {
        var result = _parser.ParseAndSort([
            ("server.test+[1]-20260101-010101-manual.tar.gz", 1),
            ("invalid.tar.gz", 2),
            ("server.test+[1]-20261231-235959-diario.tar.gz", 3),
            ("server.test+[1]-20260615-120000-antes-zerar.tar.gz", 4)
        ]);

        Assert.Equal(3, result.Count);
        Assert.Equal("server.test+[1]-20261231-235959-diario.tar.gz", result[0].FileName);
        Assert.Equal("server.test+[1]-20260615-120000-antes-zerar.tar.gz", result[1].FileName);
        Assert.Equal("server.test+[1]-20260101-010101-manual.tar.gz", result[2].FileName);
    }
}
