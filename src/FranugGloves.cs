using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Linq;
using System.Globalization;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Translations;

namespace FranugGloves;


public class ConfigGen : BasePluginConfig
{
    [JsonPropertyName("DatabaseType")]
    public string DatabaseType { get; set; } = "SQLite";
    [JsonPropertyName("DatabaseFilePath")]
    public string DatabaseFilePath { get; set; } = "/csgo/addons/counterstrikesharp/plugins/FranugGloves/franug-gloves-db.sqlite";
    [JsonPropertyName("DatabaseHost")]
    public string DatabaseHost { get; set; } = "";
    [JsonPropertyName("DatabasePort")]
    public int DatabasePort { get; set; }
    [JsonPropertyName("DatabaseUser")]
    public string DatabaseUser { get; set; } = "";
    [JsonPropertyName("DatabasePassword")]
    public string DatabasePassword { get; set; } = "";
    [JsonPropertyName("DatabaseName")]
    public string DatabaseName { get; set; } = "";
    [JsonPropertyName("Comment")]
    public string Comment { get; set; } = "use SQLite or MySQL as Database Type";
    [JsonPropertyName("ChatTag")] public string ChatTag { get; set; } = $" {ChatColors.Lime}[GlovesChooser]{ChatColors.Green} ";
}

public class FranugGloves : BasePlugin, IPluginConfig<ConfigGen>
{
    public override string ModuleName => "Franug Gloves";
    public override string ModuleAuthor => "Franc1sco Franug";
    public override string ModuleVersion => "0.0.1b";

    public ConfigGen Config { get; set; } = null!;
    public void OnConfigParsed(ConfigGen config) { Config = config; }

    internal static Dictionary<int, GloveInfo> g_playersGlove = new Dictionary<int, GloveInfo>();
    internal static Dictionary<string ,Dictionary<string, Dictionary<string, List<(string subGlove, string subGloveIndex)>>>> gloveList = new();
    private List<string> cultureList = new();
    private Dictionary<int, int> categorySelected = new();

    public override void Load(bool hotReload)
    {
        createDB();
        loadList();
        if (hotReload)
        {
            Utilities.GetPlayers().ForEach(player =>
            {
                if (!player.IsBot)
                {
                    categorySelected.Add((int)player.Index, 0);
                    getPlayerData(player);
                }
            });
        }
        RegisterEventHandler<EventPlayerConnectFull>((@event, info) =>
        {
            var player = @event.Userid;

            if (player.IsBot || !player.IsValid)
            {
                return HookResult.Continue;

            }
            else
            {
                categorySelected.Add((int)player.Index, 0);
                getPlayerData(player);
                return HookResult.Continue;
            }
        });

        RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
        {
            var player = @event.Userid;

            if (player.IsBot || !player.IsValid)
            {
                return HookResult.Continue;

            }
            else
            {
                if (g_playersGlove.ContainsKey((int)player.Index))
                {
                    g_playersGlove.Remove((int)player.Index);
                }
                if (categorySelected.ContainsKey((int)player.Index))
                {
                    categorySelected.Remove((int)player.Index);
                }
                return HookResult.Continue;
            }
        });

        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
    }

    private void loadList()
    {
        var filePath = "";
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            filePath = Server.GameDirectory + $"/csgo/addons/counterstrikesharp/plugins/FranugGloves/gloves/gloves_{culture.Name}.json";

            if (File.Exists(filePath))
            {
                cultureList.Add(culture.Name);
                string json = File.ReadAllText(filePath);

                Dictionary<string, Dictionary<string, List<(string subGlove, string subGloveIndex)>>> result = GetCategoriesWithSubGloves(json);

                foreach (var categoryEntry in result)
                {
                    // Console.WriteLine($"Categoría: {categoryEntry.Key}");
                    // Console.WriteLine($"Índice de la Categoría: {categoryEntry.Value["index"][0].subGloveIndex}");

                    foreach (var subGloveTuple in categoryEntry.Value["subGloves"])
                    {
                        // Console.WriteLine($"  Sub Glove: {subGloveTuple.subGlove}");
                        // Console.WriteLine($"  Índice del Sub Glove: {subGloveTuple.subGloveIndex}");
                    }

                    // Console.WriteLine();
                }

                gloveList[culture.Name] = result;
            }
        }
    }

    private Dictionary<string, Dictionary<string, List<(string subGlove, string subGloveIndex)>>> GetCategoriesWithSubGloves(string jsonString)
    {
        JObject jsonData = JObject.Parse(jsonString);
        var result = new Dictionary<string, Dictionary<string, List<(string subGlove, string subGloveIndex)>>>();

        foreach (JProperty gloveType in jsonData["Gloves"])
        {
            var category = gloveType.Name;
            var categoryIndex = gloveType.Value["index"].ToString();
            var subGlovesList = new List<(string subGlove, string subGloveIndex)>();

            foreach (JProperty subGlove in gloveType.Value.Children())
            {
                if (subGlove.Name != "index")
                {
                    var subGloveName = subGlove.Name;
                    var subGloveIndex = subGlove.Value["index"].ToString();
                    subGlovesList.Add((subGloveName, subGloveIndex));
                }
            }

            var categoryData = new Dictionary<string, List<(string subGlove, string subGloveIndex)>>()
            {
                { "index", new List<(string, string)> { ("", categoryIndex) } },
                { "subGloves", subGlovesList }
            };

            result.Add(category, categoryData);
        }

        return result;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        CCSPlayerController? player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot)
        {
            return HookResult.Continue;
        }

        if (g_playersGlove.TryGetValue((int)player.Index, out var gloveInfo) && gloveInfo.Model > 0)
        {

            applyGloves(player, gloveInfo.Model, gloveInfo.Paint);
        }

        return HookResult.Continue;
    }


    [ConsoleCommand("css_gloves", "")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void CommandGloves(CCSPlayerController? player, CommandInfo info)
    {
        var menu = new ChatMenu("Category");
        menu.AddMenuOption("No gloves model", (player, option) => {

            if (g_playersGlove.ContainsKey((int)player.Index))
            {
                g_playersGlove.Remove((int)player.Index);
            }
            player.PrintToChat(Config.ChatTag + $"Set gloves model to {ChatColors.Lime}" + "none" + $" {ChatColors.Green}on your next spawn");
            updatePlayer(player, 0, 0);
        });
        var culture = getValidLang(player);
        Dictionary<string, Dictionary<string, List<(string subGlove, string subGloveIndex)>>> result = gloveList[culture];

        foreach (var categoryEntry in result)
        {
            menu.AddMenuOption(categoryEntry.Key, (player, option) => {

                categorySelected[(int)player.Index] = Convert.ToInt32(categoryEntry.Value["index"][0].subGloveIndex);

                selectModel(player);
            });
        }
        //menu.PostSelectAction = PostSelectAction.Close;
        MenuManager.OpenChatMenu(player, menu);

    }

    private void selectModel(CCSPlayerController player)
    {
        var menu = new ChatMenu("Model");
        menu.AddMenuOption("No gloves model", (player, option) => {

            if (g_playersGlove.ContainsKey((int)player.Index))
            {
                g_playersGlove.Remove((int)player.Index);
            }
            player.PrintToChat(Config.ChatTag + $"Set gloves model to {ChatColors.Lime}" + "none" + $" {ChatColors.Green}on your next spawn");
            updatePlayer(player, 0, 0);
        });
        var culture = getValidLang(player);
        Dictionary<string, Dictionary<string, List<(string subGlove, string subGloveIndex)>>> result = gloveList[culture];

        foreach (var categoryEntry in result)
        {
            if (categoryEntry.Value["index"][0].subGloveIndex == categorySelected[(int)player.Index].ToString())
            {
                foreach (var subGloveTuple in categoryEntry.Value["subGloves"])
                {
                    // Console.WriteLine($"  Sub Glove: {subGloveTuple.subGlove}");
                    // Console.WriteLine($"  Índice del Sub Glove: {subGloveTuple.subGloveIndex}");
                    menu.AddMenuOption(subGloveTuple.subGlove, (player, option) =>
                    {
                        
                        var skinid = Convert.ToInt32(subGloveTuple.subGloveIndex.ToString());
                        // Console.WriteLine("aplicando skin " + skinid);
                        GloveInfo gloveInfo = new GloveInfo
                        {
                            Paint = skinid,
                            Model = categorySelected[(int)player.Index]
                        };

                        updatePlayer(player, categorySelected[(int)player.Index], skinid);
                        applyGloves(player, categorySelected[(int)player.Index], skinid);
                        g_playersGlove[(int)player.Index] = gloveInfo;

                        player.PrintToChat(Config.ChatTag + $"Set gloves model to {ChatColors.Lime}" + categoryEntry.Key + " | "+ subGloveTuple.subGlove);
                    });
                }
            }
        }
        //menu.PostSelectAction = PostSelectAction.Close;
        MenuManager.OpenChatMenu(player, menu);
    }

    public static string linuxSignature = @"\x55\x48\x89\xE5\x41\x57\x41\x56\x49\x89\xFE\x41\x55\x41\x54\x49\x89\xF4\x53\x48\x83\xEC\x78";
    public static string windowsSignature = @"\x40\x53\x41\x56\x41\x57\x48\x81\xEC\x90\x00\x00\x00\x0F\x29\x74\x24\x70";

    public static MemoryFunctionVoid<CAttributeList, string, float> CAttributeList_SetOrAddAttributeValueByNameFunc =
        new(RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? linuxSignature : windowsSignature);
    public static Action<CAttributeList, string, float> SetOrAddAttributeValueByName = CAttributeList_SetOrAddAttributeValueByNameFunc.Invoke;

    public static string linuxSignature2 = @"\x55\x48\x89\xE5\x41\x56\x49\x89\xF6\x41\x55\x41\x89\xD5\x41\x54\x49\x89\xFC\x48\x83\xEC\x08";
    public static string windowsSignature2 = @"\x48\x89\x5C\x24\x08\x48\x89\x74\x24\x10\x57\x48\x83\xEC\x20\x41\x8B\xF8\x48\x8B\xF2\x48\x8B\xD9\xE8\x2A\x2A\x2A\x2A";

    public static MemoryFunctionVoid<CCSPlayerPawn, string, long> CBaseModelEntity_SetBodygroupFunc =
        new(RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? linuxSignature2 : windowsSignature2);
    public static Action<CCSPlayerPawn, string, long> CBaseModelEntity_SetBodygroup = CBaseModelEntity_SetBodygroupFunc.Invoke;

    private void applyGloves(CCSPlayerController player, int glove, int paint)
    {
        // Console.WriteLine("aplicando");
        CCSPlayerPawn? pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
            return;

        string model = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName ?? string.Empty;
        if (!string.IsNullOrEmpty(model))
        {
            pawn.SetModel("characters/models/tm_jumpsuit/tm_jumpsuit_varianta.vmdl");
            pawn.SetModel(model);
        }
        // Console.WriteLine("aplicando2");
        AddTimer(0.06f, () =>
        {
            if (!player.IsValid)
                return;

            CCSPlayerPawn? pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                return;
            var gloves = player.PlayerPawn!.Value!.EconGloves;
            // Console.WriteLine("aplicando3");
            gloves.ItemDefinitionIndex = (ushort)glove;
            int negativeNumber = -1;
            gloves.ItemIDLow = (uint)negativeNumber;
            gloves.ItemIDLow = (16384 & 0xFFFFFFFF);
            Server.NextFrame(() =>
            {
                SetOrAddAttributeValueByName(player.PlayerPawn!.Value!.EconGloves.NetworkedDynamicAttributes, "set item texture prefab", (float)paint);
                SetOrAddAttributeValueByName(player.PlayerPawn!.Value!.EconGloves.NetworkedDynamicAttributes, "set item texture seed", 0.1f);
                SetOrAddAttributeValueByName(player.PlayerPawn!.Value!.EconGloves.NetworkedDynamicAttributes, "set item texture wear", 0.1f);

                gloves.Initialized = true;
                CBaseModelEntity_SetBodygroup(player.PlayerPawn!.Value!, "default_gloves", 1);
            });
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }


    #region database

    internal static MySqlConnection? connectionMySQL = null;
    private SqliteConnection? connectionSQLITE = null;
    private void createDB()
    {
        if (Config.DatabaseType != "MySQL")
        {
            CreateTableSQLite();
        }
        else
        {
            CreateTableMySQL();
        }
    }

    private void getPlayerData(CCSPlayerController player)
    {
        if (Config.DatabaseType != "MySQL")
        {
            _ = GetUserDataSQLite(player);
        }
        else
        {
            _ = GetUserDataMySQL(player);
        }
    }

    private void updatePlayer(CCSPlayerController player, int glove, int skinid)
    {
        if (Config.DatabaseType != "MySQL")
        {
            if (RecordExists(player))
            {
                _ = UpdateQueryDataSQLite(player, glove, skinid);
            }
            else
            {
                _ = InsertQueryDataSQLite(player, glove, skinid);
            }
        }
        else
        {
            if (RecordExists(player))
            {
                _ = UpdateQueryDataMySQL(player, glove, skinid);
            }
            else
            {
                _ = InsertQueryDataMySQL(player, glove, skinid);
            }
        }
    }

    private void CreateTableSQLite()
    {
        string dbFilePath = Server.GameDirectory + Config.DatabaseFilePath;

        var connectionString = $"Data Source={dbFilePath};";

        connectionSQLITE = new SqliteConnection(connectionString);

        connectionSQLITE.Open();

        var query = "CREATE TABLE IF NOT EXISTS player_gloves (steamid varchar(32) NOT NULL, glove_defindex int(64) NOT NULL, glove_paint_id int(6) NOT NULL);";

        using (SqliteCommand command = new SqliteCommand(query, connectionSQLITE))
        {
            command.CommandText = "CREATE TABLE IF NOT EXISTS player_gloves (steamid varchar(32) NOT NULL, glove_defindex int(64) NOT NULL, glove_paint_id int(6) NOT NULL);";
            command.ExecuteNonQuery();
        }
        connectionSQLITE.Close();
    }

    private void CreateTableMySQL()
    {
        var connectionString = $"Server={Config.DatabaseHost};Database={Config.DatabaseName};User Id={Config.DatabaseUser};Password={Config.DatabasePassword};";

        connectionMySQL = new MySqlConnection(connectionString);
        connectionMySQL.Open();

        using (MySqlCommand command = new MySqlCommand("CREATE TABLE IF NOT EXISTS `player_gloves` (`steamid` varchar(32) NOT NULL, `glove_defindex` int(64) NOT NULL, `glove_paint_id` int(6) NOT NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_unicode_ci;",
            connectionMySQL))
        {
            command.ExecuteNonQuery();
        }

        connectionMySQL.Close();
    }

    private bool RecordExists(CCSPlayerController player)
    {
        // Console.WriteLine("es "+ g_playersGlove.ContainsKey((int)player.Index));
        return g_playersGlove.ContainsKey((int)player.Index);
    }

    public async Task InsertQueryDataSQLite(CCSPlayerController player, int glove, int skinid)
    {
        try
        {
            await connectionSQLITE.OpenAsync();

            var query = "INSERT INTO player_gloves (steamid, glove_defindex, glove_paint_id) VALUES (@steamid, @glove_defindex, @glove_paint_id);";
            var command = new SqliteCommand(query, connectionSQLITE);

            command.Parameters.AddWithValue("@steamid", player.SteamID);
            command.Parameters.AddWithValue("@glove_defindex", glove);
            command.Parameters.AddWithValue("@glove_paint_id", skinid);

            await command.ExecuteNonQueryAsync();
            connectionSQLITE?.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Franug-Gloves] InsertQueryDataSQLite ******* An error occurred: {ex.Message}");
        }
        finally
        {
            await connectionSQLITE?.CloseAsync();
        }
    }

    public async Task UpdateQueryDataSQLite(CCSPlayerController player, int glove, int skinid)
    {
        try
        {
            await connectionSQLITE.OpenAsync();

            var query = "UPDATE player_gloves SET glove_defindex = @glove_defindex, glove_paint_id = @glove_paint_id WHERE steamid = @steamid;";
            var command = new SqliteCommand(query, connectionSQLITE);

            command.Parameters.AddWithValue("@steamid", player.SteamID);
            command.Parameters.AddWithValue("@glove_defindex", glove);
            command.Parameters.AddWithValue("@glove_paint_id", skinid);

            await command.ExecuteNonQueryAsync();
            connectionSQLITE?.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Franug-Gloves] UpdateQueryDataSQLite ******* An error occurred: {ex.Message}");
        }
        finally
        {
            await connectionSQLITE?.CloseAsync();
        }
    }

    public async Task GetUserDataSQLite(CCSPlayerController player)
    {
        try
        {
            await connectionSQLITE.OpenAsync();

            var query = "SELECT * FROM player_gloves WHERE steamid = @steamid;";

            var command = new SqliteCommand(query, connectionSQLITE);
            command.Parameters.AddWithValue("@steamid", player.SteamID);
            var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int gloveDefIndex = Convert.ToInt32(reader["glove_defindex"]);
                int skinid = Convert.ToInt32(reader["glove_paint_id"]);

                if (skinid > 0)
                {
                    GloveInfo gloveInfo = new GloveInfo
                    {
                        Paint = skinid,
                        Model = gloveDefIndex
                    };

                    g_playersGlove[(int)player.Index] = gloveInfo;

                    //Console.WriteLine($"[ws] glove index {gloveDefIndex} y gloveskin {skinid}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Franug-Gloves] GetUserDataSQLite ******* An error occurred: {ex.Message}");
        }
        finally
        {
            await connectionSQLITE?.CloseAsync();
        }
    }

    public async Task InsertQueryDataMySQL(CCSPlayerController player, int glove, int skinid)
    {
        try
        {
            await connectionMySQL.OpenAsync();

            var query = "INSERT INTO player_gloves (steamid, glove_defindex, glove_paint_id) VALUES (@steamid, @glove_defindex, @glove_paint_id);";
            var command = new MySqlCommand(query, connectionMySQL);

            command.Parameters.AddWithValue("@steamid", player.SteamID);
            command.Parameters.AddWithValue("@glove_defindex", glove);
            command.Parameters.AddWithValue("@glove_paint_id", skinid);


            await command.ExecuteNonQueryAsync();
            connectionMySQL?.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Franug-Gloves] InsertQueryDataMySQL ******* An error occurred: {ex.Message}");
        }
        finally
        {
            await connectionMySQL?.CloseAsync();
        }
    }

    public async Task UpdateQueryDataMySQL(CCSPlayerController player, int glove, int skinid)
    {
        try
        {
            await connectionMySQL.OpenAsync();

            var query = "UPDATE player_gloves SET glove_defindex = @glove_defindex, glove_paint_id = @glove_paint_id WHERE steamid = @steamid;";
            var command = new MySqlCommand(query, connectionMySQL);

            command.Parameters.AddWithValue("@steamid", player.SteamID);
            command.Parameters.AddWithValue("@glove_defindex", glove);
            command.Parameters.AddWithValue("@glove_paint_id", skinid);

            await command.ExecuteNonQueryAsync();
            connectionMySQL?.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Franug-Gloves] UpdateQueryDataMySQL ******* An error occurred: {ex.Message}");
        }
        finally
        {
            await connectionMySQL?.CloseAsync();
        }
    }

    public async Task GetUserDataMySQL(CCSPlayerController player)
    {
        try
        {
            await connectionMySQL.OpenAsync();

            var query = "SELECT * FROM player_gloves WHERE steamid = @steamid;";

            var command = new MySqlCommand(query, connectionMySQL);
            command.Parameters.AddWithValue("@steamid", player.SteamID);
            var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int gloveDefIndex = Convert.ToInt32(reader["glove_defindex"]);
                int skinid = Convert.ToInt32(reader["glove_paint_id"]);

                if (skinid > 0)
                {
                    GloveInfo gloveInfo = new GloveInfo
                    {
                        Paint = skinid,
                        Model = gloveDefIndex
                    };

                    g_playersGlove[(int)player.Index] = gloveInfo;

                    //Console.WriteLine($"[ws] glove index {gloveDefIndex} y gloveskin {skinid}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Franug-Gloves] GetUserDataMySQL ******* An error occurred: {ex.Message}");
        }
        finally
        {
            await connectionMySQL?.CloseAsync();
        }
    }

    #endregion

    private string getValidLang(CCSPlayerController player)
    {
        var currentlang = player.GetLanguage().Name.ToLower();
        if (cultureList.Find(lang => lang == currentlang) == null)
        {
            currentlang = "en";
        }

        return currentlang;
    }
}

public class GloveInfo
{
    public int Model { get; set; }
    public int Paint { get; set; }
}