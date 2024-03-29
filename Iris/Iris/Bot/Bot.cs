﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Iris.Api;
using Iris.Config;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Iris.Facebook;
using Iris.Twitter;
using Iris.Watcher;
using Update = Iris.Api.Update;

namespace Iris.Bot
{
    internal class Bot
    {
        private readonly ApplicationConfig _config;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<Bot> _logger;
        private readonly TelegramBotClient _client;
        private readonly Sender _sender;
        private readonly ChatsManager _chatsManager;
        private readonly IUpdatesValidator _validator;

        public Bot(
            ApplicationConfig config,
            ILoggerFactory loggerFactory,
            string chatsFile,
            IUpdatesValidator validator)
        {
            _config = config;
            _loggerFactory = loggerFactory;
            
            _logger = _loggerFactory.CreateLogger<Bot>();

            _client = new TelegramBotClient(config.TelegramBotConfig.Token);
            _sender = new Sender(
                _client,
                loggerFactory.CreateLogger<Sender>());

            _client.StartReceiving();
            _client.OnMessage += OnMessageReceived;
            
            _chatsManager = new ChatsManager(chatsFile);
            
            _validator = validator;

            RegisterProducers();

            _logger.LogInformation("Completed construction");
        }

        private async void OnMessageReceived(object _, MessageEventArgs args)
        {
            Message message = args.Message;
            long chatId = message.Chat.Id;
            bool containsKey = _chatsManager.ChatIds.ContainsKey(chatId);
            
            if (message.Text.Contains("הירשם"))
            {
                if (containsKey)
                {
                    await _client.SendTextMessageAsync(chatId, "כבר רשום");
                }
                else
                {
                    _chatsManager.Add(chatId);
                    await _client.SendTextMessageAsync(chatId, "עכשיו רשום");
                }
            }
            else if (message.Text.Contains("הפסק הרשמה"))
            {
                if (containsKey)
                {
                    _chatsManager.Remove(chatId);
                    await _client.SendTextMessageAsync(chatId, "מעכשיו לא רשום יותר");
                }
                else
                {
                    await _client.SendTextMessageAsync(chatId, "לא רשום בכלל");
                }
            }
            else
            {
                var keyboard = new ReplyKeyboardMarkup(new []
                {
                    new KeyboardButton("הירשם"), 
                    new KeyboardButton("הפסק הרשמה"), 
                });
                await _client.SendTextMessageAsync(chatId, "בחר", replyMarkup: keyboard);
            }
        }

        private void RegisterProducers()
        {
            foreach ((IUpdatesProvider provider, ProviderConfig config) in GetProviders())
            {
                try
                {
                    var usersWatcher = new UpdatesWatcher(
                        _loggerFactory.CreateLogger($"{provider.GetType().Name}-{nameof(UpdatesWatcher)}"),
                        provider,
                        config);

                    usersWatcher.Updates
                        .Subscribe(OnProducerUpdate);

                    _logger.LogInformation($"Subscribed to updates of the producer `{provider.GetType() .Name}`");
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Exception in RegisterProducers()");
                }
            }
        }

        private async void OnProducerUpdate(Update update)
        {
            _logger.LogInformation($"Caught new update: Id: {update.Id, -15}, Author: {update.Author.Id, -15}, Created at: {update.CreatedAt}");
            foreach (long chatId in _chatsManager.ChatIds.Keys)
            {
                await SendMessage(update, chatId);
            }
        }

        private async Task SendMessage(Update update, long chatId)
        {
            try
            {
                if (_validator.WasUpdateSent(update.Id, chatId))
                {
                    _logger.LogInformation($"Update #{update.Id} was already sent to chat #{chatId}");
                    return;
                }

                await _sender.SendAsync(update, chatId);
                
                _validator.UpdateSent(update.Id, chatId);
                
                _logger.LogInformation(
                    $"Sent new update: Id: {update.Id, -15}, ChatId: {update.Author.Id, -15}, Executed at: {DateTime.Now}");
            }
            catch (Exception e)
            {
                _logger.LogInformation(
                    e,
                    $"Failed to send update: Id: {update.Id, -15}, ChatId: {update.Author.Id, -15}, Executed at {DateTime.Now}");
            }
        }

        private Dictionary<IUpdatesProvider, ProviderConfig> GetProviders()
        {
            var providers = new Dictionary<IUpdatesProvider, ProviderConfig>();

            var twitterConfig = _config.TwitterConfig;
            if (twitterConfig.IsEnabled)
            {
                providers.Add(
                    new TwitterProvider(
                        _loggerFactory.CreateLogger<TwitterProvider>(),
                        twitterConfig),
                    twitterConfig);
            }

            var facebookConfig = _config.FacebookConfig;
            if (facebookConfig.IsEnabled)
            {
                providers.Add(
                    new FacebookProvider(
                        _loggerFactory.CreateLogger<FacebookProvider>(),
                        facebookConfig),
                    facebookConfig);
            }

            return providers;
        }
    }
}