using System.IO;

namespace GameCoverScraper.models;

public class MameDatCorruptError : IOException
{
    public MameDatCorruptError() : base("The mame.dat file is corrupted or in an invalid format.")
    {
    }

    public MameDatCorruptError(string message) : base(message)
    {
    }

    public MameDatCorruptError(string message, Exception innerException) : base(message, innerException)
    {
    }
}