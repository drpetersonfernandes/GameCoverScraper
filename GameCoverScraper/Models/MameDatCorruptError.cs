using System.IO;

namespace GameCoverScraper.models;

public class MameDatCorruptError : IOException
{
    public MameDatCorruptError(string message, Exception innerException) : base(message, innerException)
    {
    }
}
