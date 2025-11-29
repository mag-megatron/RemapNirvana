using System;
using ApplicationLayer.Services;
using Avalonia;
using Avalonia.Logging;
using AvaloniaUI;
using AvaloniaUI.Hub;
using AvaloniaUI.Services;
using Core.Interfaces;
using Infrastructure.HidHide;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SDL;
using static SDL.SDL3;

public sealed class SdlManager
{
    private static readonly Lazy<SdlManager> _instance = new(() => new SdlManager());
    public static SdlManager Instance => _instance.Value;

    private bool _initialized = false;

    private SdlManager() { }

    public void Initialize()
    {
        if (_initialized)
            return;

        SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");

        SDL_SetHint(SDL_HINT_AUTO_UPDATE_JOYSTICKS, "1");
        SDL_SetHint(SDL_HINT_JOYSTICK_THREAD, "1");

        SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD | SDL_InitFlags.SDL_INIT_JOYSTICK);
        _initialized = true;
    }
}
