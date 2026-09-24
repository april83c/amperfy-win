namespace Amperfy.Core.Model;

public class MusicFolder
{
    public int Pk { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int DirectoryCount { get; set; }
    public bool IsCached { get; set; }
    public int SongCount { get; set; }

    public int? AccountPk { get; set; }
    public virtual Account? Account { get; set; }
    public virtual ICollection<MusicDirectory> DirectoriesRaw { get; set; } = new HashSet<MusicDirectory>();
    public virtual ICollection<Song> SongsRaw { get; set; } = new HashSet<Song>();

    protected MusicFolder() { }

    public List<MusicDirectory> Directories => DirectoriesRaw.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    public List<Song> Songs => SongsRaw.ToList();
}
