using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Workbench.Desktop;

// One row of public.profiles as the profile card and editor use it.
public sealed class ProfileRow
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Bio { get; set; } = "";
    public string Pronouns { get; set; } = "";
    public string BannerColor { get; set; } = "blue";
    public string CodeTheme { get; set; } = "app";
    public string? AvatarPath { get; set; }
    public string? BannerPath { get; set; }
}

// A code theme people can pick for the rules editor (Assets/Editor/code-themes.json); "app" follows the app's light or dark theme.
public sealed record CodeThemeInfo(string Key, string Name)
{
    public static IReadOnlyList<CodeThemeInfo> Catalog { get; } =
        JsonSerializer.Deserialize<List<CodeThemeInfo>>(Shared.ReadAsset("Editor", "code-themes.json"), SupabaseClient.Json) ?? [];
}

// A badge people can be awarded (Assets/Settings/badges.json): its icon glyph, chip tone and what it's for.
public sealed record BadgeInfo(string Key, string Name, string Glyph, string Tone, string Description)
{
    public static IReadOnlyList<BadgeInfo> Catalog { get; } =
        JsonSerializer.Deserialize<List<BadgeInfo>>(Shared.ReadAsset("Settings", "badges.json"), SupabaseClient.Json) ?? [];

    public static BadgeInfo Find(string key) => Catalog.FirstOrDefault(b => b.Key == key) ?? new(key, key, "", "Grey", "");
}

// One of the nine icons a company tag can use (Assets/Settings/company-tags.json).
public sealed record CompanyTagIcon(string Key, string Name, string Glyph)
{
    public static IReadOnlyList<CompanyTagIcon> Catalog { get; } =
        JsonSerializer.Deserialize<List<CompanyTagIcon>>(Shared.ReadAsset("Settings", "company-tags.json"), SupabaseClient.Json) ?? [];

    public static CompanyTagIcon? Find(string? key) => Catalog.FirstOrDefault(i => i.Key == key);
}

// One badge someone holds (public.badge_awards), with who gave it and why.
public sealed class BadgeAward
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid UserId { get; set; }
    public string Badge { get; set; } = "";
    public string Reason { get; set; } = "";
    public string AwardedByName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    [JsonIgnore] public BadgeInfo Info => BadgeInfo.Find(Badge);
    [JsonIgnore] public string Name => Info.Name;
    [JsonIgnore] public string Glyph => Info.Glyph;
    // Text and Tone let the shared Chip / ChipText styles colour the badge.
    [JsonIgnore] public string Text => Info.Name;
    [JsonIgnore] public string Tone => Info.Tone;
    [JsonIgnore] public string Tip => $"{Info.Name}: {Info.Description}" +
        (Reason.Length > 0 ? $"\n“{Reason}”" : "") + $"\nFrom {(AwardedByName.Length > 0 ? AwardedByName : "management")}, {CreatedAt.LocalDateTime:MMM d, yyyy}";
}

// What a profile card shows: the popup from the side panel, and the live preview in the profile editor.
public sealed class ProfileCard : INotifyPropertyChanged
{
    string _name = "";
    string _pronouns = "";
    string _jobTitle = "";
    string _email = "";
    string _bio = "";
    string _bannerColor = "blue";
    ImageSource? _avatar;
    ImageSource? _banner;
    IReadOnlyList<BadgeAward> _badges = [];
    string? _tagIcon;
    string? _tagText;

    // How many badges show on the card (the newest).
    const int TopBadgeCount = 5;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get => _name; set { _name = value; Raise(); Raise(nameof(Initials)); } }
    public string Pronouns { get => _pronouns; set { _pronouns = value; Raise(); Raise(nameof(Subtitle)); } }
    public string JobTitle { get => _jobTitle; set { _jobTitle = value; Raise(); Raise(nameof(Subtitle)); } }
    public string Email { get => _email; set { _email = value; Raise(); } }
    public string Bio { get => _bio; set { _bio = value; Raise(); Raise(nameof(HasBio)); } }
    public string BannerColor { get => _bannerColor; set { _bannerColor = value; Raise(); Raise(nameof(BannerKey)); } }
    public ImageSource? Avatar { get => _avatar; set { _avatar = value; Raise(); Raise(nameof(HasAvatar)); } }
    public ImageSource? Banner { get => _banner; set { _banner = value; Raise(); Raise(nameof(HasBanner)); } }
    public IReadOnlyList<BadgeAward> Badges { get => _badges; set { _badges = value; Raise(); Raise(nameof(HasBadges)); Raise(nameof(TopBadges)); } }

    // The company tag beside the name, set by the company's admins: one of nine icons and up to four letters.
    public string? TagIcon { get => _tagIcon; set { _tagIcon = value; Raise(); Raise(nameof(TagGlyph)); Raise(nameof(HasTag)); } }
    public string? TagText { get => _tagText; set { _tagText = value; Raise(); Raise(nameof(HasTag)); } }
    public string TagGlyph => CompanyTagIcon.Find(_tagIcon)?.Glyph ?? "";
    public bool HasTag => !string.IsNullOrEmpty(_tagText) && CompanyTagIcon.Find(_tagIcon) is not null;

    // The newest five badges, shown as icons.
    public IReadOnlyList<BadgeAward> TopBadges => _badges.Take(TopBadgeCount).ToList();

    // The three pronoun choices; "" (not chosen) is also allowed by the check on profiles.pronouns.
    public static readonly string[] PronounChoices = ["he/him", "she/her", "they/them"];

    public bool HasAvatar => _avatar is not null;
    public bool HasBanner => _banner is not null;
    public bool HasBio => _bio.Trim().Length > 0;
    public bool HasBadges => _badges.Count > 0;
    public string Initials => InitialsOf(_name);
    // The colour resource behind the banner (Assets/Settings/Colors.*.xaml), e.g. "BannerTeal".
    public string BannerKey => "Banner" + char.ToUpperInvariant(_bannerColor[0]) + _bannerColor[1..];
    // "he/they · Residential appraiser"
    public string Subtitle => string.Join(" · ", new[] { _pronouns.Trim(), _jobTitle.Trim() }.Where(s => s.Length > 0));

    public ProfileCard CopyForPreview() => new()
    {
        Name = _name, Pronouns = _pronouns, JobTitle = _jobTitle, Email = _email, Bio = _bio, BannerColor = _bannerColor,
        Avatar = _avatar, Banner = _banner, Badges = _badges, TagIcon = _tagIcon, TagText = _tagText,
    };

    // "Ada Lovelace" → "AL", "ada" → "A".
    public static string InitialsOf(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => char.ToUpperInvariant(parts[0][0]).ToString(),
            _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}",
        };
    }

    void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// Pictures for profiles: decoding what Storage returns, and preparing what the person picks (cropped, scaled, compressed).
public static class ProfileImages
{
    public const string Bucket = "profile-media";
    public const int AvatarSize = 256;
    public const int BannerWidth = 900;
    public const int BannerHeight = 300;

    public static ImageSource? Decode(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or IOException) { return null; }
    }

    public const double AvatarAspect = 1;
    public const double BannerAspect = (double)BannerWidth / BannerHeight;

    // Reads a picked file at full size, for the cropper.
    public static BitmapSource Load(string file)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(file);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or IOException or UriFormatException)
        {
            throw new SupabaseException("That file isn't an image Levy can read. Pick a PNG or JPEG.");
        }
    }

    // The largest area of the given shape (width ÷ height) in the middle of the image: where the cropper starts.
    public static Int32Rect Centered(BitmapSource source, double aspect)
    {
        var width = source.PixelWidth;
        var height = (int)Math.Round(width / aspect);
        if (height > source.PixelHeight) { height = source.PixelHeight; width = (int)Math.Round(height * aspect); }
        return new Int32Rect((source.PixelWidth - width) / 2, (source.PixelHeight - height) / 2, width, height);
    }

    // A square profile picture: the chosen area, scaled to 256 × 256, as PNG.
    public static byte[] PrepareAvatar(BitmapSource source, Int32Rect area) => Crop(source, area, AvatarSize, AvatarSize, new PngBitmapEncoder());

    // A 3:1 banner: the chosen area, scaled to 900 × 300, as JPEG.
    public static byte[] PrepareBanner(BitmapSource source, Int32Rect area) => Crop(source, area, BannerWidth, BannerHeight, new JpegBitmapEncoder { QualityLevel = 85 });

    static byte[] Crop(BitmapSource source, Int32Rect area, int width, int height, BitmapEncoder encoder)
    {
        var scaled = new TransformedBitmap(new CroppedBitmap(source, area), new ScaleTransform((double)width / area.Width, (double)height / area.Height));
        encoder.Frames.Add(BitmapFrame.Create(scaled));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

// The signed-in person's own profile: loading it into a card, and saving edits (text, banner colour, pictures).
public sealed class ProfileStore(SupabaseClient client, Guid orgId)
{
    readonly Guid _me = Guid.TryParse(client.User?.Id, out var id) ? id : Guid.Empty;
    ProfileRow? _row;

    public ProfileCard Card { get; } = new();
    public ProfileRow? Row => _row;

    public async Task LoadAsync()
    {
        _row = (await client.GetAsync<ProfileRow>("profiles", $"select=id,full_name,email,bio,pronouns,banner_color,code_theme,avatar_path,banner_path&id=eq.{_me}")).FirstOrDefault();
        if (_row is null) return;
        Card.Name = _row.FullName;
        Card.Email = _row.Email.Length > 0 ? _row.Email : client.User?.Email ?? "";
        Card.Bio = _row.Bio;
        Card.Pronouns = _row.Pronouns;
        Card.BannerColor = _row.BannerColor;
        ThemeManager.SetProfile(_row.BannerColor, _row.CodeTheme);
        Card.Avatar = _row.AvatarPath is { } avatar ? ProfileImages.Decode(await client.DownloadAsync(ProfileImages.Bucket, avatar)) : null;
        Card.Banner = _row.BannerPath is { } banner ? ProfileImages.Decode(await client.DownloadAsync(ProfileImages.Bucket, banner)) : null;
        await LoadBadgesAsync();
    }

    public async Task LoadBadgesAsync() =>
        Card.Badges = await client.GetAsync<BadgeAward>("badge_awards", $"select=*&user_id=eq.{_me}&org_id=eq.{orgId}&order=created_at.desc");

    // Uploads new pictures under a fresh name (so nothing shows a stale copy), saves the profile, then removes replaced pictures.
    // The code theme as saved (or as last used on this computer, until the profile loads).
    public string CodeTheme => _row?.CodeTheme ?? ThemeManager.CodeTheme;

    public async Task SaveAsync(string name, string pronouns, string bio, string bannerColor, string codeTheme, byte[]? newAvatar, bool removeAvatar, byte[]? newBanner, bool removeBanner)
    {
        if (_row is null) throw new SupabaseException("Your profile hasn't loaded yet. Try again in a moment.");
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var avatarPath = newAvatar is not null ? $"{_me}/avatar-{stamp}.png" : removeAvatar ? null : _row.AvatarPath;
        var bannerPath = newBanner is not null ? $"{_me}/banner-{stamp}.jpg" : removeBanner ? null : _row.BannerPath;
        if (newAvatar is not null) await client.UploadAsync(ProfileImages.Bucket, avatarPath!, newAvatar, "image/png");
        if (newBanner is not null) await client.UploadAsync(ProfileImages.Bucket, bannerPath!, newBanner, "image/jpeg");

        await client.UpdateAsync<ProfileRow>("profiles", _me, new { FullName = name.Trim(), Pronouns = pronouns.Trim(), Bio = bio.Trim(), BannerColor = bannerColor, CodeTheme = codeTheme, AvatarPath = avatarPath, BannerPath = bannerPath });
        if (_row.AvatarPath is { } oldAvatar && oldAvatar != avatarPath) await client.RemoveAsync(ProfileImages.Bucket, oldAvatar);
        if (_row.BannerPath is { } oldBanner && oldBanner != bannerPath) await client.RemoveAsync(ProfileImages.Bucket, oldBanner);

        _row.FullName = name.Trim(); _row.Pronouns = pronouns.Trim(); _row.Bio = bio.Trim(); _row.BannerColor = bannerColor; _row.CodeTheme = codeTheme;
        _row.AvatarPath = avatarPath; _row.BannerPath = bannerPath;
        Card.Name = _row.FullName; Card.Pronouns = _row.Pronouns; Card.Bio = _row.Bio; Card.BannerColor = bannerColor;
        if (newAvatar is not null) Card.Avatar = ProfileImages.Decode(newAvatar); else if (removeAvatar) Card.Avatar = null;
        if (newBanner is not null) Card.Banner = ProfileImages.Decode(newBanner); else if (removeBanner) Card.Banner = null;
        ThemeManager.SetProfile(bannerColor, codeTheme);
    }
}
