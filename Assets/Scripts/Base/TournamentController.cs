using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

// Drives the tournament leaderboard UI: the animated prize pool, the synced countdown timer, and the
// 4-row leaderboard. Tournament data arrives from SocketController (on init / spin result / 5s poll) via
// the OnTournamentUpdate delegate, which this controller self-wires in Start.
//
// We always ignore the backend's topPlayers; every row is populated with our own dummy data built around
// the player's own rank. See PopulateRows for the windowing rule.
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

  // Timer countdown state (resynced from the server on every update, then ticked locally in Update).
  private bool timerRunning;
  private float remainingSeconds;

  private Tween _poolTween;

  private void Start()
  {
    socketController.OnTournamentUpdate = HandleTournamentUpdate;
  }

  private void Update()
  {
    if (!timerRunning) return;
    remainingSeconds = Mathf.Max(0f, remainingSeconds - Time.deltaTime);
    ApplyTimerText(remainingSeconds);
  }

  private void HandleTournamentUpdate(Tournament t, bool fromResult)
  {
    if (t == null) return;
    // "WIN TO RANK" is dropped only once the player has actually won (a positive win amount), not on a
    // spin that returns nothing. Once won, it stays dropped for the rest of that tournament. When a new
    // tournament starts the backend reports the player with no rank (playerRank <= 0) and no win yet, so
    // we re-arm the prompt — it shows "WIN TO RANK" + the dummy high rank again until the next win.
    if (t.playerWinAmount > 0) hasWon = true;
    else if (t.playerRank <= 0) hasWon = false;

    SyncTimer(t);
    SyncPool(t);
    PopulateRows(t);

    // Balance now rides on every tournament ping. Only refresh the text when idle so a ping can't
    // overwrite the optimistic "balance - bet" shown during a spin (spin end sets the real balance).
    if (!gameManager.IsSpinning && socketController.PlayerData != null)
      uiManager.SetPlayerBalance(socketController.PlayerData.balance);
  }

  // ===== Timer (synced across clients via serverTime) =====
  private void SyncTimer(Tournament t)
  {
    if (!t.isActive)
    {
      timerRunning = false;
      if (timerText != null) timerText.text = "TIMES UP";
      return;
    }

    remainingSeconds = Mathf.Max(0f, (t.endTime - t.serverTime) / 1000f);
    timerRunning = true;
    ApplyTimerText(remainingSeconds);
  }

  private void ApplyTimerText(float seconds)
  {
    if (timerText == null) return;
    int total = Mathf.CeilToInt(seconds);
    int mm = total / 60;
    int ss = total % 60;
    timerText.text = $"{mm:00}:{ss:00}";
  }

  // ===== Prize pool (lerps startPool -> targetPool over durationSeconds, synced via serverTime) =====
  private void SyncPool(Tournament t)
  {
    _poolTween?.Kill();

    if (!t.isActive || t.durationSeconds <= 0)
    {
      if (winText != null) winText.text = TextFormatter.FormatMoneyFixed2(t.targetPool);
      return;
    }

    float elapsed = Mathf.Clamp((t.serverTime - t.startTime) / 1000f, 0f, t.durationSeconds);
    float progress = elapsed / t.durationSeconds;
    double currentPool = t.startPool + (t.targetPool - t.startPool) * progress;
    float remaining = t.durationSeconds - elapsed;

    if (winText != null) winText.text = TextFormatter.FormatMoneyFixed2(currentPool);

    if (remaining <= 0f)
    {
      if (winText != null) winText.text = TextFormatter.FormatMoneyFixed2(t.targetPool);
      return;
    }

    double display = currentPool;
    _poolTween = DOTween.To(() => display, v =>
    {
      display = v;
      if (winText != null) winText.text = TextFormatter.FormatMoneyFixed2(v);
    }, t.targetPool, remaining).SetEase(Ease.Linear);
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
