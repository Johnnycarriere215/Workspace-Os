using System;

namespace WorkspaceOS.UI
{
    /// <summary>
    /// A hand-picked Unsplash photo. Unsplash photos are free to download and
    /// use under the Unsplash License; the CDN URL delivers a real 3840px-wide
    /// JPEG, and the page URL is kept so the photographer can be credited.
    /// </summary>
    public sealed class PhotoWallpaper
    {
        public string Title { get; init; }
        public string PhotoId { get; init; }

        /// <summary>Credit shown on the card — the photographer when verified.</summary>
        public string Photographer { get; init; } = "Unsplash";

        /// <summary>Where the credit link goes: the photographer's profile.</summary>
        public string CreditUrl { get; init; } = "https://unsplash.com/";

        public string ImageUrl(int width = 3840) =>
            $"https://images.unsplash.com/{PhotoId}?auto=format&fit=crop&w={width}&q=85";
    }

    public static class PhotoWallpapers
    {
        public static readonly PhotoWallpaper[] All =
        {
            new PhotoWallpaper
            {
                Title = "Alpine Dawn", PhotoId = "photo-1506905925346-21bda4d32df4",
                Photographer = "Bailey Zindel", CreditUrl = "https://unsplash.com/@baileyzindel"
            },
            new PhotoWallpaper { Title = "Misty Pines", PhotoId = "photo-1470071459604-3b5ec3a7fe05" },
            new PhotoWallpaper
            {
                Title = "Deep Forest", PhotoId = "photo-1441974231531-c6227db76b6e",
                Photographer = "Sebastian Unrau", CreditUrl = "https://unsplash.com/@sebastian_unrau"
            },
            new PhotoWallpaper
            {
                Title = "Star Field", PhotoId = "photo-1462331940025-496dfbfc7564",
                Photographer = "NASA", CreditUrl = "https://unsplash.com/@nasa"
            },
            new PhotoWallpaper { Title = "Snow Ridge", PhotoId = "photo-1519681393784-d120267933ba" },
            new PhotoWallpaper { Title = "Golden Coast", PhotoId = "photo-1500534314209-a25ddb2bd429" },
            new PhotoWallpaper
            {
                Title = "Ocean Swell", PhotoId = "photo-1518837695005-2083093ee35b",
                Photographer = "Matt Hardy", CreditUrl = "https://unsplash.com/@matthardy"
            },
            new PhotoWallpaper { Title = "Cliffside", PhotoId = "photo-1505142468610-359e7d316be0" }
        };
    }
}
