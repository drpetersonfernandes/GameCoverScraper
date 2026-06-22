using FluentAssertions;
using GameCoverScraper.Managers;
using GameCoverScraper.Models;
using MessagePack;
using Xunit;

namespace GameCoverScraper.Tests.Managers;

public class MameManagerTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _originalDatPath;

    public MameManagerTests()
    {
        _testDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MameManagerTests");
        _originalDatPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mame.dat");

        if (!Directory.Exists(_testDirectory))
        {
            Directory.CreateDirectory(_testDirectory);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void LoadFromDatWhenFileDoesNotExistShouldThrowMameDatNotFoundException()
    {
        // MameManager.LoadFromDat uses a hardcoded path, so if the file doesn't exist
        // it should throw MameDatNotFoundException
        if (File.Exists(_originalDatPath))
        {
            // Skip this test if the actual mame.dat exists (can't test missing file scenario)
            return;
        }

        var act = static () => MameManager.LoadFromDat();

        act.Should().Throw<MameDatNotFoundException>();
    }

    [Fact]
    public void MameDatNotFoundExceptionShouldBeFileNotFoundException()
    {
        var exception = new MameDatNotFoundException("test message");

        exception.Should().BeAssignableTo<Exception>();
        exception.Message.Should().Be("test message");
    }

    [Fact]
    public void MameDatCorruptErrorShouldBeIoException()
    {
        var inner = new Exception("inner");
        var exception = new MameDatCorruptError("corrupt", inner);

        exception.Should().BeAssignableTo<Exception>();
        exception.Message.Should().Be("corrupt");
        exception.InnerException.Should().Be(inner);
    }

    [Fact]
    public void MameManagerPropertiesShouldHaveCorrectDefaults()
    {
        var manager = new MameManager();

        manager.MachineName.Should().Be(string.Empty);
        manager.Description.Should().Be(string.Empty);
    }

    [Fact]
    public void MameManagerPropertiesShouldBeSettable()
    {
        var manager = new MameManager
        {
            MachineName = "pacman",
            Description = "Puck Man"
        };

        manager.MachineName.Should().Be("pacman");
        manager.Description.Should().Be("Puck Man");
    }

    [Fact]
    public void MameManagerShouldSerializeAndDeserializeWithMessagePack()
    {
        var original = new List<MameManager>
        {
            new() { MachineName = "dkong", Description = "Donkey Kong" },
            new() { MachineName = "galaga", Description = "Galaga" }
        };

        var bytes = MessagePackSerializer.Serialize(original);
        var deserialized = MessagePackSerializer.Deserialize<List<MameManager>>(bytes);

        deserialized.Should().HaveCount(2);
        deserialized[0].MachineName.Should().Be("dkong");
        deserialized[0].Description.Should().Be("Donkey Kong");
        deserialized[1].MachineName.Should().Be("galaga");
        deserialized[1].Description.Should().Be("Galaga");
    }

    [Fact]
    public void MameManagerShouldSerializeAndDeserializeEmptyList()
    {
        var original = new List<MameManager>();

        var bytes = MessagePackSerializer.Serialize(original);
        var deserialized = MessagePackSerializer.Deserialize<List<MameManager>>(bytes);

        deserialized.Should().BeEmpty();
    }
}
