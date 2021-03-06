using System.Collections.Generic;
using Newtonsoft.Json;

namespace Audiotica.Data.Spotify.Models
{
    public class SeveralArtists
    {
        [JsonProperty("artists")]
        public List<FullArtist> Artists { get; set; }
    }
}