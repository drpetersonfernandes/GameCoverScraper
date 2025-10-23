using System.IO;

namespace GameCoverScraper.models;

public class MameDatNotFoundException : FileNotFoundException
{
    public MameDatNotFoundException() : base("The mame.dat file could not be found.")
    {
    }

    public MameDatNotFoundException(string message) : base(message)
    {
    }

    public MameDatNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}