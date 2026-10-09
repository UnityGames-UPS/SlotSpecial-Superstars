using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Drives the tournament leaderboard UI: the animated prize pool, the countdown timer, and the 4-row
// leaderboard. The backend no longer sends tournament data, so the whole feature is simulated locally:
// a repeating cycle of an active tournament (tournamentDurationSeconds) followed by a cooldown
// (cooldownSeconds). While active, each spin's win accumulates into the player's tournament total; the
// player's rank does a random walk in [rankMin, rankMax] so it always stays at 10th or worse.
//
// Every leaderboard row is populated with our own dummy data built around the player's own rank. See
// PopulateRows for the windowing rule. The timer and prize pool are driven straight off wall-clock
// progress in Update; the rows are refreshed (via a locally built Tournament object) on rank steps,
// spins, and phase changes.
public class TournamentController : MonoBehaviour
{
  [SerializeField] private SocketController socketController;
  [SerializeField] private UIManager uiManager;
  [SerializeField] private GameManager gameManager;

  [Header("Tournament Pool / Timer")]
  [SerializeField] private TMP_Text winText;     // animated prize pool
  [SerializeField] private TMP_Text timerText;   // MM:SS while active, "TIMES UP" when inactive

  [Header("Leaderboard Rows (top -> bottom)")]
  [SerializeField] private TournamentRow[] rows; // exactly 4

  [Header("Simulation timing")]
  [SerializeField] private int tournamentDurationSeconds = 300; // 5 min active window
  [SerializeField] private int cooldownSeconds = 30;            // gap between tournaments ("TIMES UP")

  [Header("Simulated rank (player never breaks the top 10)")]
  [SerializeField] private int rankMin = 10;          // best rank the player can reach
  [SerializeField] private int rankMax = 50;          // worst rank the player can drift to
  [SerializeField] private float rankWalkInterval = 4f; // seconds between rank moves
  [SerializeField] private int rankWalkMaxStep = 3;     // max +/- rank change per move

  [Header("Simulated prize pool (lerps startPool -> targetPool over the tournament)")]
  [SerializeField] private double poolStartMin = 0;
  [SerializeField] private double poolStartMax = 0;
  [SerializeField] private double poolTargetMin = 10;  // added on top of start
  [SerializeField] private double poolTargetMax = 20;

  [Header("Dummy data")]
  [SerializeField] private int dummyBaseRank = 100; // rank shown on the bottom row when the player has no rank
  [SerializeField] private List<Sprite> avatarPool; // user-provided avatar images
  // Per-rank random win gap used to build dummy win values around the player's own win (rows above the
  // player get more, rows below get less). Each step between adjacent rows is a random value in this range.
  [SerializeField] private float dummyWinStepMin = 0.5f;
  [SerializeField] private float dummyWinStepMax = 5f;

  [Header("Row colors")]
  [SerializeField] private Color playerRankColor = Color.red;
  [SerializeField] private Color otherRankColor = Color.white;

  private bool hasWon; // true once the player has won during the tournament (drops the "WIN TO RANK" prompt)
  private int lastPlayerRowIndex = -1;
  private int[] rowAvatarIndices;
  private double[] rowWinOffsets; // signed gap from the player's win per row (0 on the player's own row)
  private int[] rowRanks;         // rank number shown per row (layout differs active vs ended)

  // ===== Local simulation state =====
  private enum Phase { Active, Cooldown }
  private Phase phase = Phase.Cooldown;
  private double accumulatedWin;   // sum of this tournament's spin wins (the player's tournament total)
  private int currentRank;
  private long startTimeMs;
  private long endTimeMs;
  private double startPool;
  private double targetPool;

  private static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

  private void Start()
  {
    socketController.OnSpinResult = HandleSpinResult;
    StartCoroutine(RunTournamentCycle());
  }

  // Timer and prize pool are driven straight off wall-clock progress every frame while a tournament is
  // active. This keeps the pool lerp perfectly smooth across the whole window (no tweens to recreate)
  // and keeps the timer correct even after a reload mid-tournament.
  private void Update()
  {
    if (phase != Phase.Active) return;
    long now = NowMs;
    ApplyTimerText(Mathf.Max(0f, (endTimeMs - now) / 1000f));
    if (winText != null) winText.text = TextFormatter.FormatMoneyFixed2(CurrentPool(now));
  }

  // Repeating cycle anchored to wall-clock time: a global active window (tournamentDurationSeconds)
  // followed by a cooldown (cooldownSeconds), aligned to absolute epoch so every client/session lands on
  // the same phase. Loading mid-tournament resumes it partway instead of restarting at the full time.
  private IEnumerator RunTournamentCycle()
  {
    long cycleMs = (long)(tournamentDurationSeconds + cooldownSeconds) * 1000L;
    long activeMs = (long)tournamentDurationSeconds * 1000L;

    while (true)
    {
      long now = NowMs;
      long pos = ((now % cycleMs) + cycleMs) % cycleMs; // ms into the current global cycle
      long cycleStart = now - pos;
      startTimeMs = cycleStart;
      endTimeMs = cycleStart + activeMs;

      if (pos < activeMs)
      {
        BeginActive();

        // Active phase: walk the rank up/down every rankWalkInterval until the window elapses.
        while (NowMs < endTimeMs)
        {
          float wait = Mathf.Min(rankWalkInterval, Mathf.Max(0f, (endTimeMs - NowMs) / 1000f));
          yield return new WaitForSeconds(Mathf.Max(0.1f, wait));
          if (NowMs >= endTimeMs) break;
          StepRank();
          RefreshRows();
        }
      }
      else
      {
        // Joined during the cooldown gap — fabricate a finished pool/rank just for the display.
        PickPool();
        currentRank = UnityEngine.Random.Range(rankMin, rankMax + 1);
      }

      // Cooldown: timer shows "TIMES UP", pool freezes at its final value, standings stay on screen.
      BeginCooldown();
      long cooldownEnd = cycleStart + cycleMs;
      while (NowMs < cooldownEnd)
        yield return new WaitForSeconds(0.25f);
    }
  }

  private void BeginActive()
  {
    phase = Phase.Active;
    accumulatedWin = 0;
    hasWon = false; // re-arm "WIN TO RANK" for the new tournament (rank is always >= rankMin now)
    currentRank = UnityEngine.Random.Range(rankMin, rankMax + 1);
    PickPool();
    RefreshRows(); // timer + pool are then driven each frame by Update
  }

  private void BeginCooldown()
  {
    phase = Phase.Cooldown;
    if (timerText != null) timerText.text = "TIMES UP";
    if (winText != null) winText.text = TextFormatter.FormatMoneyFixed2(targetPool);
    RefreshRows();
  }

  private void PickPool()
  {
    startPool = UnityEngine.Random.Range((float)poolStartMin, (float)poolStartMax);
    targetPool = startPool + UnityEngine.Random.Range((float)poolTargetMin, (float)poolTargetMax);
  }

  // Current prize-pool value as a linear lerp startPool -> targetPool over the active window.
  private double CurrentPool(long now)
  {
    if (tournamentDurationSeconds <= 0) return targetPool;
    float elapsed = Mathf.Clamp((now - startTimeMs) / 1000f, 0f, tournamentDurationSeconds);
    float progress = elapsed / tournamentDurationSeconds;
    return startPool + (targetPool - startPool) * progress;
  }

  private void StepRank()
  {
    int step = UnityEngine.Random.Range(-rankWalkMaxStep, rankWalkMaxStep + 1);
    currentRank = Mathf.Clamp(currentRank + step, rankMin, rankMax);
  }

  // Adds the just-finished spin's win to the tournament total. Only counts while a tournament is active.
  private void HandleSpinResult()
  {
    if (phase != Phase.Active) return;
    double win = socketController.ResultData?.payload?.currentWinning ?? 0;
    if (win > 0)
    {
      accumulatedWin += win;
      hasWon = true;
    }
    RefreshRows();
  }

  // Builds the local Tournament snapshot and refreshes only the leaderboard rows (timer + pool are
  // handled by Update). Called on tournament start, each rank step, each spin, and on cooldown entry.
  private void RefreshRows()
  {
    Tournament t = new Tournament
    {
      isActive = phase == Phase.Active,
      serverTime = NowMs,
      startTime = startTimeMs,
      endTime = endTimeMs,
      durationSeconds = tournamentDurationSeconds,
      startPool = startPool,
      targetPool = targetPool,
      playerRank = currentRank,
      playerWinAmount = accumulatedWin
    };

    PopulateRows(t);
  }

  private void ApplyTimerText(float seconds)
  {
    if (timerText == null) return;
    int total = Mathf.CeilToInt(seconds);
    int mm = total / 60;
    int ss = total % 60;
    timerText.text = $"{mm:00}:{ss:00}";
  }

  // ===== Leaderboard rows =====
  // Two layouts depending on whether the tournament is still running:
  //  * Active: a 4-rank window around the player. Ranks 1-3 put the player on the matching top row
  //    (window 1-4); otherwise the player sits on the bottom row with the 3 better ranks above.
  //  * Ended: the final top 3 always fill the first rows; the player's own rank takes the bottom row
  //    (e.g. #50), unless the player finished in the top 3, in which case they take that row.
  // When the player has no rank (-1) we fall back to dummyBaseRank.
  private void PopulateRows(Tournament t)
  {
    if (rows == null || rows.Length == 0) return;
    int n = rows.Length;
    if (rowRanks == null || rowRanks.Length != n) rowRanks = new int[n];

    int effectiveRank = t.playerRank > 0 ? t.playerRank : dummyBaseRank;
    int playerRowIndex;

    if (t.isActive)
    {
      playerRowIndex = effectiveRank <= 3 ? effectiveRank - 1 : n - 1;
      playerRowIndex = Mathf.Clamp(playerRowIndex, 0, n - 1);
      int topRank = effectiveRank - playerRowIndex;
      for (int i = 0; i < n; i++) rowRanks[i] = topRank + i;
    }
    else
    {
      // Final standings: first rows are the top finishers; the last row carries the player's rank.
      playerRowIndex = effectiveRank <= 3 ? effectiveRank - 1 : n - 1;
      playerRowIndex = Mathf.Clamp(playerRowIndex, 0, n - 1);
      for (int i = 0; i < n; i++)
        rowRanks[i] = i < n - 1 ? i + 1 : (effectiveRank <= 3 ? n : effectiveRank);
    }

    // Re-roll the dummy avatars + win gaps whenever the player moves to a different row.
    if (playerRowIndex != lastPlayerRowIndex)
    {
      RerollDummyData(playerRowIndex);
      lastPlayerRowIndex = playerRowIndex;
    }

    for (int i = 0; i < n; i++)
    {
      TournamentRow row = rows[i];
      if (row == null) continue;

      bool isPlayer = i == playerRowIndex;
      int rank = rowRanks[i];

      if (row.rankText != null)
      {
        row.rankText.text = "#" + rank;
        row.rankText.color = isPlayer ? playerRankColor : otherRankColor;
      }

      if (row.avatar != null && avatarPool != null && avatarPool.Count > 0 && rowAvatarIndices != null)
        row.avatar.sprite = avatarPool[rowAvatarIndices[i]];

      // The "YOU" text and the red avatar overlay both mark the current player's row.
      if (row.youIndicator != null) row.youIndicator.SetActive(isPlayer);
      if (row.redOverlay != null) row.redOverlay.SetActive(isPlayer);

      // Player row shows the real win. Dummy rows always carry a win value anchored to the player's win
      // (rows above show more, rows below less) so their ranking order makes sense at all times.
      double winValue;
      if (isPlayer) winValue = t.playerWinAmount;
      else if (rowWinOffsets == null) winValue = 0;
      else winValue = Math.Max(0, t.playerWinAmount + rowWinOffsets[i]);
      if (row.winValueText != null) row.winValueText.text = TextFormatter.FormatMoney(winValue);

      // "WIN TO RANK" only appears on the player's own row, only while the tournament is active, and only
      // until their first spin. Otherwise (inactive tournament, or after spinning) show "POINTS" + value.
      bool showWinToRank = isPlayer && !hasWon && t.isActive;
      if (row.winToRankLabel != null) row.winToRankLabel.SetActive(showWinToRank);
      if (row.pointsLabel != null) row.pointsLabel.SetActive(!showWinToRank);
      if (row.winValueText != null) row.winValueText.gameObject.SetActive(!showWinToRank);
    }
  }

  private void RerollDummyData(int playerRowIndex)
  {
    if (rows == null) return;
    int n = rows.Length;

    if (rowAvatarIndices == null || rowAvatarIndices.Length != n) rowAvatarIndices = new int[n];
    if (rowWinOffsets == null || rowWinOffsets.Length != n) rowWinOffsets = new double[n];

    if (avatarPool != null && avatarPool.Count > 0)
      for (int i = 0; i < n; i++) rowAvatarIndices[i] = UnityEngine.Random.Range(0, avatarPool.Count);

    // Win gaps fan out from the player's row: each step up adds a random amount, each step down subtracts
    // one, so the column stays ordered (higher win = better rank). The player's own row stays at 0 (exact).
    rowWinOffsets[playerRowIndex] = 0;
    double acc = 0;
    for (int i = playerRowIndex - 1; i >= 0; i--)
    {
      acc += UnityEngine.Random.Range(dummyWinStepMin, dummyWinStepMax);
      rowWinOffsets[i] = acc;
    }
    acc = 0;
    for (int i = playerRowIndex + 1; i < n; i++)
    {
      acc -= UnityEngine.Random.Range(dummyWinStepMin, dummyWinStepMax);
      rowWinOffsets[i] = acc;
    }
  }
}

[Serializable]
public class TournamentRow
{
  public Image avatar;
  public TMP_Text rankText;       // "#97"
  public TMP_Text winValueText;   // player row shows real win; other rows 0.00
  public GameObject pointsLabel;     // static "POINTS"
  public GameObject winToRankLabel;  // static "WIN TO RANK"
  public GameObject youIndicator;    // "YOU" text shown on the current player's row
  public GameObject redOverlay;      // red image over the avatar, marks the current player's row
}
