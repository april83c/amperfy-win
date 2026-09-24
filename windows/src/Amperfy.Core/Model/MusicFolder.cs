using Microsoft.EntityFrameworkCore.ChangeTracking;
namespace Amperfy.Core.Model;

public class MusicFolder
{
    public virtual int Pk { get; set; }
    public virtual string Id { get; set; } = "";
    public virtual string Name { get; set; } = "";
    public virtual int DirectoryCount { get; set; }
    public virtual bool IsCached { get; set; }
    public virtual int SongCount { get; set; }

    public virtual int? AccountPk { get; set; }
    public virtual Account? Account { get; set; }
    public virtual ICollection<MusicDirectory> DirectoriesRaw { get; set; } = new ObservableHashSet<MusicDirectory>();
    public virtual ICollection<Song> SongsRaw { get; set; } = new ObservableHashSet<Song>();

    protected MusicFolder() { }

    public List<MusicDirectory> Directories => DirectoriesRaw.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    public List<Song> Songs => SongsRaw.ToList();
}
