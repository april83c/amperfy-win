using Amperfy.Core.Api;

namespace Amperfy.Core.Model;

public class Radio : AbstractPlayable
{
    public virtual string? SiteUrl { get; set; }

    protected Radio() { }

    public override DerivedPlayableType DerivedType => DerivedPlayableType.Radio;

    public override string CreatorName => "";

    public string Identifier => Title;

    public override List<string> InfoDetails(ServerApiType? api, DetailInfoType details)
    {
        var info = new List<string>();
        if (details.Type != DetailType.Long) return info;
        if (!string.IsNullOrEmpty(SiteUrl)) info.Add($"Site {SiteUrl}");
        if (!string.IsNullOrEmpty(Url)) info.Add($"Stream URL {Url}");
        return info;
    }

    public override bool IsAvailableToUser() => RemoteStatus == RemoteStatus.Available;
}
