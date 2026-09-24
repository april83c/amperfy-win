namespace Amperfy.Core.Storage;

public sealed class EqualizerSetting : IEquatable<EqualizerSetting>
{
    /// Frequencies in Hz
    public static readonly float[] Frequencies = [32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
    public static float[] DefaultGains => [0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    public const int RangeFromZero = 6;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";

    /// EQ gain within 6 dB range
    public float[] Gains { get; set; } = DefaultGains;

    public EqualizerSetting() { }

    public EqualizerSetting(string name, float[]? gains = null, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Name = name;
        Gains = gains ?? DefaultGains;
    }

    public static readonly Guid OffId = Guid.Parse("00000000-0000-0000-0000-00000000e0ff");
    public static EqualizerSetting Off => new("Off", DefaultGains, OffId);

    /// Automatic gain compensation to maintain consistent volume levels
    public float GainCompensation
    {
        get
        {
            var positive = Gains.Where(g => g > 0).ToArray();
            var avgBoost = positive.Length == 0 ? 0 : positive.Average();
            return -Math.Min(avgBoost / 2.0f, 6.0f);
        }
    }

    /// Compensated output volume (1.0 = normal, &lt;1.0 = reduced to compensate for EQ boost)
    public float CompensatedVolume => Math.Clamp(1.0f + GainCompensation / 20.0f, 0.1f, 2.0f);

    public bool Equals(EqualizerSetting? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as EqualizerSetting);
    public override int GetHashCode() => Id.GetHashCode();
    public override string ToString() => Name;
}

public enum EqualizerPreset
{
    Off = 0,
    IncreasedBass = 1,
    ReducedBass = 2,
    IncreasedTreble = 3,
}

public static class EqualizerPresetExtensions
{
    public static string Description(this EqualizerPreset p) => p switch
    {
        EqualizerPreset.Off => "Off",
        EqualizerPreset.IncreasedBass => "Increased Bass",
        EqualizerPreset.ReducedBass => "Reduced Bass",
        _ => "Increased Treble",
    };

    public static float[] Gains(this EqualizerPreset p) => p switch
    {
        EqualizerPreset.Off => [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        EqualizerPreset.IncreasedBass => [5, 4, 3, 2, 0, -1, -2, -3, -3, -3],
        EqualizerPreset.ReducedBass => [-3, -2, -1, -1, 0, 0, 0, 0, 0, 0],
        _ => [0, 0, 0, 0, 1, 2, 3, 4, 5, 6],
    };

    public static EqualizerSetting AsEqualizerSetting(this EqualizerPreset p) => new(p.Description(), p.Gains());
}
