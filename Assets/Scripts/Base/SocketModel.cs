using System;
using System.Collections.Generic;

[Serializable]
public class AuthTokenData
{
  public string cookie;
  public string socketURL;
  public string nameSpace;
}

[Serializable]
public class Root
{
  public string id;
  public bool success;
  public GameData gameData;
  public Features features;
  public Tournament tournament;
  public UiData uiData;
  public Player player;
  public List<List<string>> matrix;
  public Payload payload;
}

[Serializable]
public class GameData
{
  public List<double> bets;
}

[Serializable]
public class Features
{
  public WinMultiplierConfig winMultiplier;
  public AnyPayouts anyPayouts;
}

// Last-reel win multiplier feature (e.g. 2x/4x/10x/25x on the configured reel).
[Serializable]
public class WinMultiplierConfig
{
  public List<int> values;
  public bool enabled;
  public List<int> weights;
  public int reelIndex;
  public string description;
}

// Flat payouts for "any" combinations.
[Serializable]
public class AnyPayouts
{
  public double anyBars;
  public double anyMixed;
  public double anySevens;
  public double oneStarSeven;
  public double twoStarSevens;
}

[Serializable]
public class Tournament
{
  public string tournamentId;
  public bool isActive;
  public long startTime;
  public long currentTime;
  public long endTime;        // epoch ms
  public long serverTime;     // epoch ms — server "now"; used to sync the timer + pool lerp across clients
  public int durationSeconds;
  public double startPool;
  public double targetPool;
  public List<TopPlayer> topPlayers; // ignored — leaderboard rows use our own dummy data
  public int playerRank;
  public double playerScore;
  public double playerWinAmount;
}

[Serializable]
public class TopPlayer
{
  public string id;
  public string name;
  public double score;
}

[Serializable]
public class UiData
{
  public Paylines paylines;
}

[Serializable]
public class Paylines
{
  public List<Symbol> symbols;
}

[Serializable]
public class Symbol
{
  public int id;
  public string name;
  public double payout;
  public string group;
  public int multiplierValue; // only present on multiplier symbols; defaults to 0 otherwise
  public List<int> multiplier;
  public string description;
}

[Serializable]
public class Player
{
  public double balance;
}

[Serializable]
public class Payload
{
  public double currentWinning;
  public Win win;
}

[Serializable]
public class Win
{
  public string type;
  public int symbolId;
  public int multiplier;
  public double baseWin;
}

// Spin request payload
[Serializable]
public class MessageData
{
  public string type;
  public Data payload = new();
}

[Serializable]
public class Data
{
  public int betIndex;
}
