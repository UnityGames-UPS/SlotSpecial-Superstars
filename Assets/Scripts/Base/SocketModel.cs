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
  public UiData uiData;
  public Player player;
  public List<List<string>> matrix;
  public Payload payload;
}

[Serializable]
public class GameData
{
  public List<List<int>> lines;
  public List<double> bets;
}

[Serializable]
public class Features
{
  public FreeSpinsConfig freeSpins;
  public Dictionary<int, int> diamondPayout; // keys "2".."9" -> multiplier
}

[Serializable]
public class FreeSpinsConfig
{
  public int spins;
  public bool retrigger;
  public int triggerCount;
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
  public double winAmount;
  public List<LineWin> lineWins;
  public FeatureWins featureWins;
  public List<object> triggeredFeatures;     // TODO: element type unknown (empty in sample)
  public FreeSpinsState freeSpins;
}

[Serializable]
public class FeatureWins
{
  public double diamondWin;
  public int diamondCount;
  public List<string> diamondPositions;
}

[Serializable]
public class FreeSpinsState
{
  public int awarded;
  public int total;
  public bool active;
  public int remaining;
  public List<string> scatterPositions;
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

[Serializable]
public class LineWin
{
  public int lineIndex;
  public string symbolName;
  public double payout;
  public double winAmount;
  public List<string> positions;   // "row,col"
}
