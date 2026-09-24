using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Amperfy.Core.Storage;

/// EF Core context (port of the CoreData model v49). Uses lazy loading proxies; all library
/// entities are stored in one TPH table ("LibraryEntities") like CoreData's entity inheritance.
public sealed class AmperfyDbContext : DbContext
{
    private readonly string _connectionString;
    private readonly System.Data.Common.DbConnection? _connection;

    public AmperfyDbContext(string connectionString, System.Data.Common.DbConnection? openConnection = null)
    {
        _connectionString = connectionString;
        _connection = openConnection;
        SavingChanges += (_, _) => UpdateDenormalizedCounts();
        ChangeTracker.Tracked += (_, e) =>
        {
            if (e.Entry.Entity is Playlist playlist) playlist.ItemFactory ??= () => this.CreateProxy<PlaylistItem>();
        };
    }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AbstractLibraryEntity> LibraryEntities => Set<AbstractLibraryEntity>();
    public DbSet<AbstractPlayable> Playables => Set<AbstractPlayable>();
    public DbSet<Song> Songs => Set<Song>();
    public DbSet<PodcastEpisode> PodcastEpisodes => Set<PodcastEpisode>();
    public DbSet<Radio> Radios => Set<Radio>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<MusicDirectory> Directories => Set<MusicDirectory>();
    public DbSet<Podcast> Podcasts => Set<Podcast>();
    public DbSet<Artwork> Artworks => Set<Artwork>();
    public DbSet<EmbeddedArtwork> EmbeddedArtworks => Set<EmbeddedArtwork>();
    public DbSet<Model.Download> Downloads => Set<Model.Download>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistItem> PlaylistItems => Set<PlaylistItem>();
    public DbSet<MusicFolder> MusicFolders => Set<MusicFolder>();
    public DbSet<ScrobbleEntry> ScrobbleEntries => Set<ScrobbleEntry>();
    public DbSet<SearchHistoryItem> SearchHistoryItems => Set<SearchHistoryItem>();
    public DbSet<LogEntry> LogEntries => Set<LogEntry>();
    public DbSet<PlayerState> PlayerStates => Set<PlayerState>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (_connection is not null) optionsBuilder.UseSqlite(_connection);
        else optionsBuilder.UseSqlite(_connectionString);
        optionsBuilder
            .UseLazyLoadingProxies()
            // entities notify their changes: DetectChanges doesn't have to compare snapshots of the
            // whole tracked library on every save (a large library made every save take seconds)
            .UseChangeTrackingProxies()
            .UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
    }

    private static readonly Type[] EntityTypes =
    [
        typeof(Account), typeof(AbstractLibraryEntity), typeof(AbstractPlayable), typeof(Song), typeof(PodcastEpisode),
        typeof(Radio), typeof(Album), typeof(Artist), typeof(Genre), typeof(MusicDirectory), typeof(Podcast), typeof(Artwork),
        typeof(EmbeddedArtwork), typeof(Model.Download), typeof(Playlist), typeof(PlaylistItem), typeof(MusicFolder),
        typeof(ScrobbleEntry), typeof(SearchHistoryItem), typeof(LogEntry), typeof(PlayerState),
    ];

    protected override void OnModelCreating(ModelBuilder b)
    {
        // change-tracking proxies, but keep the original values (count updates and the save error
        // recovery need them)
        b.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues);
        // Computed (getter-only) properties are never mapped; EF would otherwise discover
        // computed collections like Album.Songs as navigations.
        foreach (var type in EntityTypes)
        {
            foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (prop.SetMethod is null) b.Entity(type).Ignore(prop.Name);
            }
        }

        // --- keys ---
        b.Entity<Account>().HasKey(e => e.Pk);
        b.Entity<AbstractLibraryEntity>().HasKey(e => e.Pk);
        b.Entity<Artwork>().HasKey(e => e.Pk);
        b.Entity<EmbeddedArtwork>().HasKey(e => e.Pk);
        b.Entity<Model.Download>().HasKey(e => e.Pk);
        b.Entity<Playlist>().HasKey(e => e.Pk);
        b.Entity<PlaylistItem>().HasKey(e => e.Pk);
        b.Entity<MusicFolder>().HasKey(e => e.Pk);
        b.Entity<ScrobbleEntry>().HasKey(e => e.Pk);
        b.Entity<SearchHistoryItem>().HasKey(e => e.Pk);
        b.Entity<LogEntry>().HasKey(e => e.Pk);
        b.Entity<PlayerState>().HasKey(e => e.Pk);

        // --- library entity hierarchy (TPH) ---
        b.Entity<AbstractLibraryEntity>(e =>
        {
            e.ToTable("LibraryEntities");
            e.HasDiscriminator<string>("Kind")
                .HasValue<Song>("Song")
                .HasValue<PodcastEpisode>("PodcastEpisode")
                .HasValue<Radio>("Radio")
                .HasValue<Album>("Album")
                .HasValue<Artist>("Artist")
                .HasValue<Genre>("Genre")
                .HasValue<MusicDirectory>("Directory")
                .HasValue<Podcast>("Podcast");
            e.Property<string>("Kind").HasMaxLength(16);
            e.HasIndex("Kind", nameof(AbstractLibraryEntity.AccountPk), nameof(AbstractLibraryEntity.Id));
            e.HasIndex(nameof(AbstractLibraryEntity.Id));
            e.Property(x => x.Id).UseCollation("BINARY");
            e.Property(x => x.AlphabeticSectionInitial).UseCollation("NOCASE");
            e.Property(x => x.Rating).HasField("_rating");
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountPk).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Artwork).WithMany(a => a.Owners).HasForeignKey(x => x.ArtworkPk).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.SearchHistory).WithOne(h => h.SearchedLibraryEntity)
                .HasForeignKey<SearchHistoryItem>(h => h.SearchedLibraryEntityPk).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AbstractPlayable>(e =>
        {
            e.Property(x => x.TitleRaw).HasColumnName("Title").UseCollation("NOCASE");
            e.HasIndex(x => x.RelFilePath);
            e.HasOne(x => x.Download).WithOne(d => d.Playable)
                .HasForeignKey<Model.Download>(d => d.PlayablePk).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.EmbeddedArtwork).WithOne(a => a.Owner)
                .HasForeignKey<EmbeddedArtwork>(a => a.OwnerPk).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.PlaylistItems).WithOne(i => i.Playable).HasForeignKey(i => i.PlayablePk).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ScrobbleEntries).WithOne(s => s.Playable).HasForeignKey(s => s.PlayablePk).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Song>(e =>
        {
            e.HasOne(x => x.Album).WithMany(a => a.SongsRaw).HasForeignKey(x => x.AlbumPk).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Artist).WithMany(a => a.SongsRaw).HasForeignKey(x => x.ArtistPk).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Directory).WithMany(d => d.SongsRaw).HasForeignKey(x => x.DirectoryPk).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Genre).WithMany(g => g.SongsRaw).HasForeignKey(x => x.GenrePk).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.MusicFolder).WithMany(f => f.SongsRaw).HasForeignKey(x => x.MusicFolderPk).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<PodcastEpisode>(e =>
        {
            e.HasOne(x => x.Podcast).WithMany(p => p.EpisodesRaw).HasForeignKey(x => x.PodcastPk).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Album>(e =>
        {
            e.Property(x => x.NameRaw).HasColumnName("Name").UseCollation("NOCASE");
            e.HasOne(x => x.Artist).WithMany(a => a.AlbumsRaw).HasForeignKey(x => x.ArtistPk).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Genre).WithMany(g => g.AlbumsRaw).HasForeignKey(x => x.GenrePk).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Artist>(e =>
        {
            e.Property(x => x.NameRaw).HasColumnName("Name").UseCollation("NOCASE");
            e.HasOne(x => x.Genre).WithMany(g => g.ArtistsRaw).HasForeignKey(x => x.GenrePk).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Genre>(e => e.Property(x => x.NameRaw).HasColumnName("Name").UseCollation("NOCASE"));

        b.Entity<Podcast>(e => e.Property(x => x.TitleRaw).HasColumnName("Title").UseCollation("NOCASE"));

        b.Entity<MusicDirectory>(e =>
        {
            e.Property(x => x.NameRaw).HasColumnName("Name").UseCollation("NOCASE");
            e.HasOne(x => x.MusicFolder).WithMany(f => f.DirectoriesRaw).HasForeignKey(x => x.MusicFolderPk).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Parent).WithMany(d => d.SubdirectoriesRaw).HasForeignKey(x => x.ParentPk).OnDelete(DeleteBehavior.SetNull);
        });

        // --- other entities ---
        b.Entity<Account>(e => e.HasIndex(x => new { x.ServerHashRaw, x.UserHashRaw }));

        b.Entity<Artwork>(e =>
        {
            e.HasIndex(x => new { x.AccountPk, x.Id });
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountPk).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Download).WithOne(d => d.Artwork)
                .HasForeignKey<Model.Download>(d => d.ArtworkPk).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EmbeddedArtwork>(e => e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountPk).OnDelete(DeleteBehavior.SetNull));

        b.Entity<Model.Download>(e =>
        {
            e.HasIndex(x => new { x.AccountPk, x.Id });
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountPk).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Playlist>(e =>
        {
            e.HasIndex(x => new { x.AccountPk, x.Id });
            e.Property(x => x.NameRaw).HasColumnName("Name").UseCollation("NOCASE");
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountPk).OnDelete(DeleteBehavior.SetNull);
            // Items are owned by the playlist: removing an item from the collection deletes it.
            e.HasMany(x => x.ItemsRaw).WithOne(i => i.Playlist).HasForeignKey(i => i.PlaylistPk)
                .IsRequired().OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ArtworkItemsRaw).WithOne(i => i.PlaylistArtworkItem).HasForeignKey(i => i.PlaylistArtworkItemPk)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.SearchHistory).WithOne(h => h.SearchedPlaylist)
                .HasForeignKey<SearchHistoryItem>(h => h.SearchedPlaylistPk).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PlaylistItem>(e =>
        {
            e.HasIndex(x => new { x.PlaylistPk, x.Order });
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountPk).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<MusicFolder>(e =>
        {
            e.HasIndex(x => new { x.AccountPk, x.Id });
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountPk).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ScrobbleEntry>(e => e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountPk).OnDelete(DeleteBehavior.SetNull));

        b.Entity<SearchHistoryItem>(e => e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountPk).OnDelete(DeleteBehavior.SetNull));

        b.Entity<PlayerState>(e =>
        {
            e.HasOne(x => x.ContextPlaylist).WithMany().HasForeignKey(x => x.ContextPlaylistPk).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ShuffledContextPlaylist).WithMany().HasForeignKey(x => x.ShuffledContextPlaylistPk).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.UserQueuePlaylist).WithMany().HasForeignKey(x => x.UserQueuePlaylistPk).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.PodcastPlaylist).WithMany().HasForeignKey(x => x.PodcastPlaylistPk).OnDelete(DeleteBehavior.SetNull);
        });
    }

    // --- denormalized counts (CoreData updated them in willSave) --------------------------------

    private void UpdateDenormalizedCounts()
    {
        ChangeTracker.DetectChanges();
        var albums = new HashSet<Album>();
        var artists = new HashSet<Artist>();
        var genres = new HashSet<Genre>();
        var directories = new HashSet<MusicDirectory>();
        var folders = new HashSet<MusicFolder>();
        var podcasts = new HashSet<Podcast>();
        var playlists = new HashSet<Playlist>();

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            switch (entry.Entity)
            {
                case Song s:
                    Collect(entry, () => s.Album, nameof(Song.AlbumPk), albums);
                    Collect(entry, () => s.Artist, nameof(Song.ArtistPk), artists);
                    Collect(entry, () => s.Genre, nameof(Song.GenrePk), genres);
                    Collect(entry, () => s.Directory, nameof(Song.DirectoryPk), directories);
                    Collect(entry, () => s.MusicFolder, nameof(Song.MusicFolderPk), folders);
                    break;
                case Album a:
                    Collect(entry, () => a.Artist, nameof(Album.ArtistPk), artists);
                    Collect(entry, () => a.Genre, nameof(Album.GenrePk), genres);
                    break;
                case Artist ar:
                    Collect(entry, () => ar.Genre, nameof(Artist.GenrePk), genres);
                    break;
                case MusicDirectory d:
                    Collect(entry, () => d.Parent, nameof(MusicDirectory.ParentPk), directories);
                    Collect(entry, () => d.MusicFolder, nameof(MusicDirectory.MusicFolderPk), folders);
                    break;
                case PodcastEpisode ep:
                    Collect(entry, () => ep.Podcast, nameof(PodcastEpisode.PodcastPk), podcasts);
                    break;
                case PlaylistItem pi:
                    Collect(entry, () => pi.Playlist, nameof(PlaylistItem.PlaylistPk), playlists);
                    break;
            }
        }

        foreach (var a in albums) SetIfChanged(a.SongCountRaw, Live(a.SongsRaw), v => a.SongCountRaw = v);
        foreach (var a in artists)
        {
            SetIfChanged(a.SongCountRaw, Live(a.SongsRaw), v => a.SongCountRaw = v);
            SetIfChanged(a.AlbumCountRaw, Live(a.AlbumsRaw), v => a.AlbumCountRaw = v);
        }
        foreach (var g in genres)
        {
            SetIfChanged(g.SongCountRaw, Live(g.SongsRaw), v => g.SongCountRaw = v);
            SetIfChanged(g.AlbumCountRaw, Live(g.AlbumsRaw), v => g.AlbumCountRaw = v);
            SetIfChanged(g.ArtistCountRaw, Live(g.ArtistsRaw), v => g.ArtistCountRaw = v);
        }
        foreach (var d in directories)
        {
            SetIfChanged(d.SongCountRaw, Live(d.SongsRaw), v => d.SongCountRaw = v);
            SetIfChanged(d.SubdirectoryCountRaw, Live(d.SubdirectoriesRaw), v => d.SubdirectoryCountRaw = v);
        }
        foreach (var f in folders)
        {
            SetIfChanged(f.SongCount, Live(f.SongsRaw), v => f.SongCount = v);
            SetIfChanged(f.DirectoryCount, Live(f.DirectoriesRaw), v => f.DirectoryCount = v);
        }
        foreach (var p in podcasts) SetIfChanged(p.EpisodeCountRaw, Live(p.EpisodesRaw), v => p.EpisodeCountRaw = v);
        foreach (var p in playlists) SetIfChanged(p.SongCountRaw, Live(p.ItemsRaw), v => p.SongCountRaw = v);
    }

    private int Live<T>(ICollection<T> items) where T : class =>
        items.Count(i => Entry(i).State != EntityState.Deleted);

    private static void SetIfChanged(int current, int value, Action<int> set)
    {
        if (current != value) set(value);
    }

    private void Collect<TParent>(EntityEntry entry, Func<TParent?> currentParentGetter, string fkProperty, HashSet<TParent> target) where TParent : class
    {
        if (entry.State == EntityState.Modified)
        {
            var prop = entry.Property(fkProperty);
            if (!prop.IsModified) return;
            if (prop.OriginalValue is int originalPk && FindTracked<TParent>(originalPk) is { } original &&
                Entry(original).State != EntityState.Deleted)
                target.Add(original);
        }
        var currentParent = currentParentGetter();
        if (currentParent is not null && Entry(currentParent).State != EntityState.Deleted) target.Add(currentParent);
    }

    private TParent? FindTracked<TParent>(int pk) where TParent : class
    {
        foreach (var e in ChangeTracker.Entries<TParent>())
        {
            if (e.Property("Pk").CurrentValue is int p && p == pk) return e.Entity;
        }
        return Find<TParent>(pk);
    }
}
