﻿using Lavalink4NET;
using Discord.WebSocket;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Players;
using Microsoft.Extensions.Options;
using Discord;
using Lavalink4NET.Tracks;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Rest;

namespace PlexBot.Core.LavaLink
{
    public class LavaLinkCommands(IAudioService audioService, DiscordSocketClient discordClient, Players.Players visualPlayer)
    {
        private readonly IAudioService _audioService = audioService;
        private readonly DiscordSocketClient _discordClient = discordClient;
        private readonly Players.Players _players = visualPlayer;

        public async Task<CustomPlayer?> GetPlayerAsync(SocketInteraction interaction, bool connectToVoiceChannel = true)
        {
            if (interaction.User is not SocketGuildUser user || user.VoiceChannel == null)
            {
                await interaction.FollowupAsync("You must be in a voice channel to play music.").ConfigureAwait(false);
                return null;
            }
            ulong guildId = user.Guild.Id;
            ulong voiceChannelId = user.VoiceChannel.Id;
            PlayerChannelBehavior channelBehavior = connectToVoiceChannel ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None;
            PlayerRetrieveOptions retrieveOptions = new(channelBehavior);
            CustomPlayerOptions options = new()
            {
                DisconnectOnStop = false,
                TextChannel = interaction.Channel as ITextChannel
            };
            IOptions<CustomPlayerOptions> optionsWrapper = Options.Create(options);
            PlayerFactory<CustomPlayer, CustomPlayerOptions> factory = (properties, options) =>
            {
                return new ValueTask<CustomPlayer>(new CustomPlayer(properties, this));
            };
            PlayerResult<CustomPlayer> result = await _audioService.Players
                .RetrieveAsync<CustomPlayer, CustomPlayerOptions>(guildId, voiceChannelId, factory, optionsWrapper, retrieveOptions)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                string errorMessage = result.Status switch
                {
                    PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
                    PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected.",
                    _ => "Unknown error.",
                };
                await interaction.FollowupAsync(errorMessage).ConfigureAwait(false);
                return null;
            }
            return result.Player;
        }

        public async Task PlayMedia(SocketInteraction interaction, CustomTrackQueueItem track)
        {
            QueuedLavalinkPlayer? player = await GetPlayerAsync(interaction, true);
            if (player == null)
            {
                Console.WriteLine("Player not found.");
                return;
            }
            TrackPlayProperties playProperties = new()
            {
                NoReplace = true,
            };
            if (!string.IsNullOrEmpty(track.Url))
            {
                //Console.WriteLine($"Playing Track - Title: {track.Title}, URL: {track.Url}"); // Debugging
                try
                {
                    await player.PlayAsync(track, playProperties).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to play track: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("Invalid track URL.");
            }
        }

        public async Task<string> TogglePauseResume(SocketInteraction interaction)
        {
            QueuedLavalinkPlayer? player = await GetPlayerAsync(interaction, true);
            if (player == null)
            {
                return "Player not found.";
            }
            if (player.State == PlayerState.Paused)
            {
                await player.ResumeAsync();
                return "Resumed.";
            }
            else
            {
                await player.PauseAsync();
                return "Paused.";
            }
        }

        public async Task DisplayQueueAsync(SocketInteraction interaction)
        {
            var player = await GetPlayerAsync(interaction, true);
            if (player == null)
            {
                //await RespondAsync("No queued player is currently active.", ephemeral: true);
                return;
            }
            if (player.Queue.IsEmpty)
            {
                //await RespondAsync("The queue is currently empty.", ephemeral: true);
                return;
            }
            var embed = new EmbedBuilder()
                .WithTitle("Current Music Queue")
                .WithDescription("Here are the details of the tracks in the queue:");
            foreach (var item in player.Queue)
            {
                // Access track details
                var track = item.Track;
                //embed.AddField(track.Title, $"Artist: {track.Author}\nDuration: {TimeSpan.FromMilliseconds(track.Duration).ToString(@"hh\:mm\:ss")}\nURL: [Listen]({track.Uri})");
            }

            //await RespondAsync(embed: embed.Build(), ephemeral: true);
        }

        //public async Task<List<Dictionary<string, string>>> GetQueueInfo()
        //{
        //    QueuedLavalinkPlayer? player = await GetPlayerAsync(interaction, true);
        //    List<Dictionary<string, string>> queueInfo = [];
        //    // Access the queue from the player
        //    ITrackQueue tracks = player.Queue;
        //    // Iterate through all tracks in the queue
        //    foreach (ITrackQueue track in tracks)
        //    {
        //        if (track is CustomTrackQueueItem customTrack)
        //        {
        //            // Create a dictionary for each custom track
        //            Dictionary<string, string> trackInfo = new()
        //            {
        //                ["Title"] = customTrack.Title ?? "Unknown Title",
        //                ["Duration"] = customTrack.Duration ?? "00:00",
        //                ["Artist"] = customTrack.Artist ?? "Unknown Artist",
        //                ["Album"] = customTrack.Album ?? "Unknown Album",
        //                ["Studio"] = customTrack.Studio ?? "Unknown Studio",
        //                ["Artwork"] = customTrack.Artwork ?? "https://via.placeholder.com/150",
        //                ["Url"] = customTrack.Url ?? string.Empty
        //            };
        //            // Add the dictionary to the list
        //            queueInfo.Add(trackInfo);
        //            Console.WriteLine($"Track: {trackInfo["Title"]}, Artist: {trackInfo["Artist"]}, Duration: {trackInfo["Duration"]}"); // debug
        //        }
        //        else
        //        {
        //            Console.WriteLine("Error: Track is not a CustomTrackQueueItem.");
        //        }
        //    }
        //    return await Task.FromResult(queueInfo);
        //}

        public async Task AddToQueue(SocketInteraction interaction, List<Dictionary<string, string>> trackDetailsList)
        {
            QueuedLavalinkPlayer? player = await GetPlayerAsync(interaction, true);
            if (player == null)
            {
                Console.WriteLine("Player not found.");
                return;
            }
            foreach (var details in trackDetailsList)
            {
                string trackUrl = details["Url"];
                if (!string.IsNullOrEmpty(trackUrl))
                {
                    LavalinkTrack? lavalinkTrack = await _audioService.Tracks.LoadTrackAsync(
                        trackUrl, TrackSearchMode.None);
                    if (lavalinkTrack != null)
                    {
                        CustomTrackQueueItem customTrack = new()
                        {
                            Reference = new TrackReference(lavalinkTrack),
                            Title = details["Title"],
                            Artist = details["Artist"],
                            Album = details["Album"],
                            ReleaseDate = details["ReleaseDate"],
                            Artwork = details["Artwork"],
                            Url = trackUrl,
                            ArtistUrl = details["ArtistUrl"],
                            Duration = details["Duration"],
                            Studio = details["Studio"]
                        };
                        await PlayMedia(interaction, customTrack);
                    }
                    else
                    {
                        Console.WriteLine($"Could not load track from URL: {trackUrl}");
                    }
                }
            }
        }
    }

    public class CustomTrackQueueItem : ITrackQueueItem
    {
        public TrackReference Reference { get; set; }
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? ReleaseDate { get; set; }
        public string? Artwork { get; set; }
        public string? Url { get; set; }
        public string? ArtistUrl { get; set; }
        public string? Duration { get; set; }
        public string? Studio { get; set; }

        public T? As<T>() where T : class, ITrackQueueItem => this as T;
    }
}
