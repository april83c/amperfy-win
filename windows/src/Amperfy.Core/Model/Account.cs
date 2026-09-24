namespace Amperfy.Core.Model;

public class Account
{
    public virtual int Pk { get; set; }
    public virtual BackendApiType ApiType { get; set; } = BackendApiType.NotDetected;
    public virtual string? IdRaw { get; set; }
    public virtual string? ServerHashRaw { get; set; }
    public virtual string? ServerUrlRaw { get; set; }
    public virtual string? UserHashRaw { get; set; }
    public virtual string? UserNameRaw { get; set; }

    protected Account() { }

    internal void AssignAccount(string serverUrl, string userName, BackendApiType apiType)
    {
        ServerUrlRaw = serverUrl;
        UserNameRaw = userName;
        AssignInfo(AccountInfo.Create(serverUrl, userName, apiType));
    }

    public void AssignInfo(AccountInfo info)
    {
        ServerHashRaw = info.ServerHash;
        UserHashRaw = info.UserHash;
        ApiType = info.ApiType;
    }

    public string Id => IdRaw ?? "";
    public string ServerHash => ServerHashRaw ?? "";
    public string UserHash => UserHashRaw ?? "";
    public string ServerUrl => ServerUrlRaw ?? "";
    public string UserName => UserNameRaw ?? "";
    public string Ident => $"{ServerHash}-{UserHash}";
    public string ShortLogIdent => $"{ServerHash[..Math.Min(4, ServerHash.Length)]}{UserHash[..Math.Min(2, UserHash.Length)]}";
    public AccountInfo Info => new(ServerHash, UserHash, ApiType);

    public override string ToString() => $"Account({Ident})";
}
