﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlexBot.Core.PlexAPI
{
    public class PlexAuth
    {
        readonly string clientIdentifierKey = Environment.GetEnvironmentVariable("CLIENT_IDENTIFIER_KEY") ?? "";
        readonly string plexAppName = Environment.GetEnvironmentVariable("PLEX_APP_NAME") ?? "";
        readonly HttpClient httpClient = new();

        public async Task<bool> VerifyStoredAccessTokenAsync(string accessToken)
        {
            string requestUrl = "https://plex.tv/api/v2/user";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("accept", "application/json");
            request.Headers.Add("X-Plex-Product", plexAppName);
            request.Headers.Add("X-Plex-Client-Identifier", clientIdentifierKey);
            request.Headers.Add("X-Plex-Token", accessToken);
            HttpResponseMessage response = await httpClient.SendAsync(request);
            return response.StatusCode == System.Net.HttpStatusCode.OK;
        }

        public async Task<(int pinId, string pinCode)> GeneratePinAsync()
        {
            string requestUrl = "https://plex.tv/api/v2/pins";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Add("accept", "application/json");
            request.Headers.Add("X-Plex-Product", plexAppName);
            request.Headers.Add("X-Plex-Client-Identifier", clientIdentifierKey);
            request.Content = new StringContent("strong=true", Encoding.UTF8, "application/x-www-form-urlencoded");
            HttpResponseMessage response = await httpClient.SendAsync(request);
            string responseString = await response.Content.ReadAsStringAsync();
            JsonDocument jsonDoc = JsonDocument.Parse(responseString);
            int pinId = jsonDoc.RootElement.GetProperty("id").GetInt32();
            string pinCode = jsonDoc.RootElement.GetProperty("code").GetString();
            Console.WriteLine($"Pin ID: {pinId}, Pin Code: {pinCode}");
            return (pinId, pinCode);
        }

        public string ConstructAuthAppUrl(int pinId, string pinCode, string forwardUrl)
        {
            return $"https://app.plex.tv/auth#?clientID={clientIdentifierKey}&code={pinCode}&context%5Bdevice%5D%5Bproduct%5D={Uri.EscapeDataString(plexAppName)}&forwardUrl={Uri.EscapeDataString(forwardUrl)}";
        }

        public async Task<string> CheckPinAsync(int pinId, string pinCode)
        {
            string requestUrl = $"https://plex.tv/api/v2/pins/{pinId}";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("accept", "application/json");
            request.Headers.Add("X-Plex-Client-Identifier", clientIdentifierKey);
            HttpResponseMessage response = await httpClient.SendAsync(request);
            string responseString = await response.Content.ReadAsStringAsync();
            JsonDocument jsonDoc = JsonDocument.Parse(responseString);

            if (jsonDoc.RootElement.TryGetProperty("authToken", out JsonElement authTokenProperty))
            {
                return authTokenProperty.GetString();
            }
            return null;
        }

        public async Task<string> GetAccessTokenAsync()
        {
            string accessToken = Environment.GetEnvironmentVariable("PLEX_TOKEN") ?? "";
            if (string.IsNullOrEmpty(accessToken) || !await VerifyStoredAccessTokenAsync(accessToken))
            {
                // Generate new token if invalid or not available
                (int pinId, string pinCode) = await GeneratePinAsync();
                string authUrl = ConstructAuthAppUrl(pinId, pinCode, "http://localhost/");
                Console.WriteLine("Please authenticate at the following URL:");
                Console.WriteLine(authUrl);
                string newAccessToken = null;
                while (newAccessToken == null)
                {
                    await Task.Delay(1000); // Poll every second
                    newAccessToken = await CheckPinAsync(pinId, pinCode);
                }
                Environment.SetEnvironmentVariable("PLEX_TOKEN", newAccessToken);
                string envFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
                if (File.Exists(envFilePath))
                {
                    List<string> lines = File.ReadAllLines(envFilePath).ToList();
                    int tokenIndex = lines.FindIndex(line => line.StartsWith("PLEX_TOKEN="));
                    if (tokenIndex >= 0)
                    {
                        lines[tokenIndex] = $"PLEX_TOKEN={newAccessToken}";
                    }
                    else
                    {
                        lines.Add($"PLEX_TOKEN={newAccessToken}");
                    }
                    File.WriteAllLines(envFilePath, lines);
                }
                return newAccessToken;
            }
            return accessToken;
        }
    }
}