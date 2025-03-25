using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TwoRatChat.Main.Clients;

internal static class WoWnikTwitchClient
{
    public static async Task<dynamic> GetInfoAsync(string userName)
    {
        var url = $"https://twitch.wownik.ru/Rest/Helix/GetUsers?query={userName.ToLower()}";

        App.Log(' ', "Getting Twitch user info");
        try
        {
            using var client = new HttpClient();

            var response = await client.GetAsync(url).ConfigureAwait(false);
            var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            dynamic data = JsonConvert.DeserializeObject(responseString);
            return data;
        }
        catch (Exception e)
        {
            App.Log('!', "Getting Twitch user info error: {0}", e);
        }

        return null;
    }
}