using System.Text;
using Tarkovy.Services;

var url = args.Length > 0 ? args[0] : "";
var key = args.Length > 1 ? args[1] : "";
if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
{
    Console.Error.WriteLine("Usage: EncryptSquadHost <url> <anon-key>");
    return 1;
}

static string CsBytes(byte[] data)
{
    var sb = new StringBuilder();
    sb.AppendLine("[");
    for (var i = 0; i < data.Length; i++)
    {
        if (i % 16 == 0)
            sb.Append("        ");
        sb.Append($"0x{data[i]:X2}");
        if (i < data.Length - 1)
            sb.Append(',');
        if (i % 16 == 15 || i == data.Length - 1)
            sb.AppendLine();
        else
            sb.Append(' ');
    }
    sb.Append("    ]");
    return sb.ToString();
}

var urlBlob = SquadSecretBox.Seal(url.Trim().TrimEnd('/'));
var keyBlob = SquadSecretBox.Seal(key.Trim());
if (SquadSecretBox.Open(urlBlob) != url.Trim().TrimEnd('/') || SquadSecretBox.Open(keyBlob) != key.Trim())
{
    Console.Error.WriteLine("round-trip failed");
    return 2;
}

Console.WriteLine("""
namespace Tarkovy.Services;

public static partial class SquadHost
{
    static partial void LoadOfficial(ref string url, ref string anonKey)
    {
        url = SquadSecretBox.TryOpen(UrlBlob);
        anonKey = SquadSecretBox.TryOpen(KeyBlob);
    }

    static readonly byte[] UrlBlob =
""");
Console.WriteLine(CsBytes(urlBlob) + ";");
Console.WriteLine();
Console.WriteLine("    static readonly byte[] KeyBlob =");
Console.WriteLine(CsBytes(keyBlob) + ";");
Console.WriteLine("}");
return 0;
