using KeBiao.App;

namespace KeBiao.Core.Tests;

public class UiStateTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kebiao-ui-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Theory]
    [InlineData(-2, -2)]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    public void MigratesLegacyFontLevels(int original, int expected)
    {
        File.WriteAllText(Path.Combine(_dir, "ui.json"), $$"""{"FontLevel":{{original}},"Left":120,"Locked":true}""");

        var state = UiState.Load(_dir);
        Assert.Equal(expected, state.FontLevel);
        Assert.Equal(1, state.FontScheme);
        Assert.Equal(120d, state.Left);
        Assert.True(state.Locked);

        state.Save(_dir);
        Assert.Equal(expected, UiState.Load(_dir).FontLevel);
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(2)]
    [InlineData(3)]
    public void KeepsSavedSevenLevelFont(int level)
    {
        File.WriteAllText(Path.Combine(_dir, "ui.json"), $$"""{"FontScheme":1,"FontLevel":{{level}}}""");

        Assert.Equal(level, UiState.Load(_dir).FontLevel);
    }

    [Fact]
    public void NewSettingsUseCurrentScheme() => Assert.Equal(1, UiState.Load(_dir).FontScheme);
}
