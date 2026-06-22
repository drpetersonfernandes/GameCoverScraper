namespace GameCoverScraper.Models;

public class MameDatNotFoundException : Exception
{
    public MameDatNotFoundException(string message) : base(message)
    {
    }
}
