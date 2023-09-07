namespace MovieDbWithProxy.Models
{
  public class TmdbImageSettings
  {
    public List<string> backdrop_sizes { get; set; }

    public string secure_base_url { get; set; }

    public List<string> poster_sizes { get; set; }

    public List<string> profile_sizes { get; set; }

    public List<string> logo_sizes { get; set; }

    public List<string> still_sizes { get; set; }

    public string GetImageUrl(string image) => secure_base_url + image;

    public string GetOriginalImageUrl(string image) => GetImageUrl("original") + image;

    public string GetPosterThumbnailImageUrl(string image)
    {
      if (poster_sizes != null)
      {
        string image1 = poster_sizes.ElementAtOrDefault(1) ?? poster_sizes.FirstOrDefault();
        if (!string.IsNullOrEmpty(image1))
          return GetImageUrl(image1) + image;
      }
      return GetOriginalImageUrl(image);
    }

    public string GetStillThumbnailImageUrl(string image) => this.GetOriginalImageUrl(image);

    public string GetProfileThumbnailImageUrl(string image)
    {
      if (this.profile_sizes != null)
      {
        string image1 = this.profile_sizes.ElementAtOrDefault<string>(1) ?? this.profile_sizes.FirstOrDefault<string>();
        if (!string.IsNullOrEmpty(image1))
          return this.GetImageUrl(image1) + image;
      }
      return this.GetOriginalImageUrl(image);
    }

    public string GetLogoThumbnailImageUrl(string image)
    {
      if (this.logo_sizes != null)
      {
        string image1 = this.logo_sizes.ElementAtOrDefault<string>(3) ?? this.logo_sizes.ElementAtOrDefault<string>(2) ?? this.logo_sizes.ElementAtOrDefault<string>(1) ?? this.logo_sizes.LastOrDefault<string>();
        if (!string.IsNullOrEmpty(image1))
          return this.GetImageUrl(image1) + image;
      }
      return this.GetOriginalImageUrl(image);
    }

    public string GetBackdropThumbnailImageUrl(string image)
    {
      if (this.backdrop_sizes != null)
      {
        string image1 = this.backdrop_sizes.FirstOrDefault<string>();
        if (!string.IsNullOrEmpty(image1))
          return this.GetImageUrl(image1) + image;
      }
      return this.GetOriginalImageUrl(image);
    }
  }
}
