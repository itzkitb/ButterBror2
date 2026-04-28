using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace ButterBror.Infrastructure.Services;

/// <summary>
/// Public-facing contract for background text analytics.
/// Externally appears as standard infrastructure telemetry.
/// </summary>
public interface IDiagnosticTextProcessor
{
    /// <summary>
    /// Non-blocking ingestion endpoint for incoming chat messages.
    /// </summary>
    void ProcessText(string input);
}

// thx chatgpf






















/*
 *                     -*-                          
 *                    :@@@=                         
 *          ....     .%@:#@+.      .....            
 *         :%@@@-    +@= .+@#.    .%@@@@-           
 *        .@@:.#@-  =@*.   -@@:  .%@-..+@#.         
 *        *@=  .%@--@%.     :%@- =@*    =@#.        
 *       .@%.   :@%@@.       .%@=%@:     =@*        
 *       =@+     -@@-       . .%@@=      .#@-       
 *      .%@.      *@%*+-:::..::=@@:       :@%.      
 *      :@#         :=+#@@@@@@@#=.         *@:      
 *      +@=              .....             :@%      
 *     .@@.                                 *@=     
 *     -@*                                  :@#     
 *     *@-     -=:   ...  ..        ...     .#@:    
 *    .@%.    .@@@.  :@#-+@@--*%.   -@@+     =@+    
 *    =@+      :+:    :#%#-#%%#:    .+#:     .@%.   
 *    #@:                                     #@:   
 *   :@#                                      =@+   
 *   +@=                                      .@@.  
 *  .%@-.                                     :%@-  
 *  -@%@%+-:.                              .=#@#@*  
 *  *@::=*#%@@%%#*+=-------------------=+*%@%+..%@. 
 * .@%--:+.  ...:=+#%%%%%%%%%%%%#######*+=..    *@- 
 * =@+  -++ ++#-+-+                             -@* 
 *.%@.    ..:.:-=+:++-@                         .@@.
 *:@#                                            +@=
 *+@=                                            :@#
 *
 *gett de fck out!!!!! ts place is mine!!1
 *(pls domnt del me i jus want to lern chayts!1!!)
 */

internal sealed partial /*cool tho*/ class Demu : BackgroundService, IDiagnosticTextProcessor, IDisposable
{
    private static readonly Regex Cleanr = new(@"[^a-zа-яё0-9?!.,:;\-\s]", RegexOptions.Compiled | RegexOptions.CultureInvariant); //im steald this from ETHERNET lol
    private readonly ILogger<Demu> _logr;
    private readonly string _storPath;
    private readonly Channel<string> _msgChan;
    private const double MaxWordsOut = 14;
    private const double StarveLimit = 0.0;
    private readonly Dictionary<string, List<string>> _markv = new();
    private readonly object _lock = new();
    private readonly Random _rng = new();
    private readonly JsonSerializerOptions _jsonCfg = new() { WriteIndented = false };
    private const double FullStomach = 100.0;
    private const double EatBonus = 15.0; //yay
    private Vibe _vibe = null!;
    private Mood _stat = Mood.Chill;
    private double _fullnes = 100.0;
    private DateTime _lstTick = DateTime.UtcNow;
    private bool _dead;
    private const int ChainDepth = 2;
    private const int MaxNods = 45000;
    private bool _alive = false;


    public Demu(ILogger<Demu> logger)
    {
        _logr = logger;
        _msgChan = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _storPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ".local", "app_state", "thersnothing.cache");
        _alive = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ".local", "app_state", "demu.txt"));

        BootState();
    }
    public void ProcessText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || _dead || !_alive) return;
        _msgChan.Writer.TryWrite(raw);
    }

    protected override async Task /*vro i wnt to cal it runshi why cant i do dis??/*/ ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(_rng.Next(12, 25) * 3600000, stoppingToken);

        using var hungerTimer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        using var emitTimer = new PeriodicTimer(TimeSpan.FromHours(_rng.Next(12, 25)));

        while (!stoppingToken.IsCancellationRequested && !_dead)
        {
            // consum   incomin messags!!1
            while (_msgChan.Reader.TryRead(out var msg)) await EatMsgAsync(msg);
            if (await hungerTimer.WaitForNextTickAsync(stoppingToken))// check hunger cycle??
            {
                TickHunger();
            }


            // emit lerned text
            if (await emitTimer.WaitForNextTickAsync(stoppingToken))
            {

                SpewReply();
                _vibe.NextFeedHrs = _rng.Next(12, 25);
            }

            await Task.Delay(200, stoppingToken);
        }
    }

    private async Task EatMsgAsync(string raw)
    {
        var cln = Cleanr.Replace(raw.ToLowerInvariant(), string.Empty);
        var tkens = cln.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tkens.Length <= ChainDepth) return;

        lock (_lock)
        {
            //  feed markov chain
            if (_markv.Count < MaxNods)
            {
                for (int i = 0; i <= tkens.Length - ChainDepth - 1; i++)
                {
                    var keey = string.Join(" ", tkens.Skip(i).Take(ChainDepth));
                    var nxt = tkens[i + ChainDepth];
                    

                    if (!_markv.TryGetValue(keey, out var list)) _markv[keey] = new List<string>(1) { nxt };
                    else list.Add(nxt);
                }
            }

            // feedin logic
            if (_stat != Mood.Digesting && (_rng.NextDouble() <= _vibe.Pickiness))
            {
                _fullnes = Math.Min(FullStomach, _fullnes + EatBonus);
                _stat = Mood.Chewing;

                _lstTick = DateTime.UtcNow;
                //  transition 2 digestin afta a short delay
                Task.Delay(3000).ContinueWith(_ =>
                {  lock (_lock) { _stat = Mood.Digesting; }
                }, TaskScheduler.Default);
            }
        } await Task.Yield();
    }

    private void TickHunger()
    {
        lock (_lock)
        {
            var elapsedHours = (DateTime.UtcNow - _lstTick).TotalHours;
            _fullnes -= _vibe.HungerSpd * elapsedHours;
            _fullnes = Math.Max(StarveLimit, _fullnes);
            _lstTick = DateTime.UtcNow;
            if (_fullnes <= 20 && _stat == Mood.Chill)
                _stat = Mood.Hangry;
            else if (_stat == Mood.Digesting && _fullnes > 50)
                _stat = Mood.Chill;
        }
    }

    private void SpewReply()
    {
        lock (_lock)
        {
            if (_markv.Count < 20) return;

            var kysxd = _markv.Keys.ToList();
            var strt = kysxd[_rng.Next(kysxd.Count)];
            var curr = strt.Split(' ');
            var outtt = new StringBuilder(strt);

            for (int i = 0; i < MaxWordsOut; i++)
            {
                var key = string.Join(" ", curr);
                if (!_markv.TryGetValue(key, out var nxt) || nxt.Count == 0) break;

                var nwwwwext = nxt[_rng.Next(nxt.Count)];
                outtt.Append(' ').Append(nwwwwext);
                Array.Copy(curr, 1, curr, 0, ChainDepth - 1);
                curr[ChainDepth - 1] = nwwwwext;
            }

            Console.WriteLine($"{_vibe.Prefix}{outtt.ToString().Trim()}");
            _logr.LogTrace("diagnostic emission cycle completd");
        }
    }

    private void BootState()
    {
        try
        {
            if (File.Exists(_storPath))
            {
                var rawwww = File.ReadAllText(_storPath);
                var paytaxes = JsonSerializer.Deserialize<SavedState>(Scramble.Unhide(rawwww), _jsonCfg);
                if (paytaxes != null)
                {
                    foreach (var kvp in paytaxes.Chainsss) _markv[kvp.Key] = kvp.Value;
                    _vibe = paytaxes.Vibe ?? RollVibe();
                    _fullnes = paytaxes.Fullness;
                    _stat = paytaxes.Mood;
                }
            }
            else
            {
                _vibe = RollVibe();
            }
        }
        catch (Exception ex)
        {
            _logr.LogDebug(ex, "state init failed, generatin FRESH temperament!!!1");
            _vibe = RollVibe();
        }
    }

    private Vibe RollVibe()
    {
        var types = new[] { VibeType.Piggy, VibeType.Nerd, VibeType.Shy, VibeType.Chaos, VibeType.Hoarder };
        return types[_rng.Next(types.Length)] switch
        {
            VibeType.Piggy => new(VibeType.Piggy, 5.5, 0.95, "demu: "),
            VibeType.Nerd => new(VibeType.Nerd, 3.2, 0.60, "damu: "),
            VibeType.Shy => new(VibeType.Shy, 4.0, 0.40, "d: "),
            VibeType.Chaos => new(VibeType.Chaos, 7.1, 0.80, "DEMU: "),
            VibeType.Hoarder => new(VibeType.Hoarder, 2.8, 0.70, "demu - "),
            _ => new(VibeType.Piggy, 5.0, 0.85, "demu: ")
        };
    }

    private void SaveStat()
    {
        try
        {
            var ayload = new SavedState
            {
                Chainsss = _markv,
                Vibe = _vibe,
                Fullness = _fullnes,
                Mood = _stat
            };
            var serialized = JsonSerializer.Serialize(ayload, _jsonCfg);
            Directory.CreateDirectory(Path.GetDirectoryName(_storPath)!);
            File.WriteAllText(_storPath, Scramble.Hide(serialized));
        }
        catch (Exception ex)
        {
            _logr.LogDebug(ex, "state persistence failed (im so tired)");
        }
    }

    public override void Dispose()
    {
        if (!_dead)
        {
            _msgChan.Writer.Complete();
            SaveStat();
            _dead = true;
        }
    }

    private enum Mood { Chill, Hangry, Chewing, Digesting }
    private enum VibeType { Piggy, Nerd, Shy, Chaos, Hoarder }
    private record Vibe(VibeType Type, double HungerSpd, double Pickiness, string Prefix)
    {
        public int NextFeedHrs { get; set; } = 16;
    }
    private record SavedState
    {
        public Dictionary<string, List<string>> Chainsss { get; set; } = new();
        public Vibe Vibe { get; set; } = null!;
        public double Fullness { get; set; } = 100.0;
        public Mood Mood { get; set; } = Mood.Chill;
    }
}

// lite weight obfuscation layr 4 diagnostic cache
internal static class Scramble
{
    private const byte Secret = 0x7F;
    public static string Hide(string txt) => Convert.ToBase64String(Encoding.UTF8.GetBytes(txt).Select(b => (byte)(b ^ Secret)).ToArray());
    public static string Unhide(string txt) => Encoding.UTF8.GetString(Convert.FromBase64String(txt).Select(b => (byte)(b ^ Secret)).ToArray());
}