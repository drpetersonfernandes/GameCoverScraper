using System.IO;

namespace GameCoverScraper.models;

public class MameDatNotFoundException : FileNotFoundException
{
    public MameDatNotFoundException(string message) : base(message)
    {
    }
}
