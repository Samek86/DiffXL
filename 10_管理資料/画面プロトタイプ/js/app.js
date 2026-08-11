/**
 * DiffXL 画面プロトタイプ — 画面遷移・インタラクション
 * 見た目確認用。実際の Excel 解析や OpenCV 比較は行わない。
 */

(function () {
  "use strict";

  /** 状態 */
  const state = {
    leftPath: "",
    rightPath: "",
    comparing: false,
    syncScroll: true,
  };

  /** 画面・オーバーレイ要素 */
  const screens = {
    startup: document.getElementById("screen-startup"),
    main: document.getElementById("screen-main"),
  };

  const overlays = {
    sheetMap: document.getElementById("overlay-sheet-map"),
    anchor: document.getElementById("overlay-anchor"),
    replace: document.getElementById("overlay-replace"),
    settings: document.getElementById("overlay-settings"),
  };

  const els = {
    leftPathStartup: document.getElementById("startup-left-path"),
    rightPathStartup: document.getElementById("startup-right-path"),
    leftPathMain: document.getElementById("main-left-path"),
    rightPathMain: document.getElementById("main-right-path"),
    leftViewport: document.getElementById("left-viewport"),
    rightViewport: document.getElementById("right-viewport"),
    minimap: document.getElementById("minimap"),
    minimapViewport: document.getElementById("minimap-viewport"),
    statusText: document.getElementById("status-text"),
    statusDiff: document.getElementById("status-diff"),
    toast: document.getElementById("toast"),
    loading: document.getElementById("loading-mask"),
    replaceSide: document.getElementById("replace-side"),
    replacePath: document.getElementById("replace-path"),
  };

  /** デモ用のダミーパス */
  const demoPaths = {
    left: "C:\\Data\\Sample\\仕様書_旧版.xlsx",
    right: "C:\\Data\\Sample\\仕様書_新版.xlsx",
  };

  /**
   * 指定画面を表示する
   * @param {"startup"|"main"} name 画面名
   */
  function showScreen(name) {
    Object.values(screens).forEach((el) => el.classList.remove("active"));
    if (screens[name]) {
      screens[name].classList.add("active");
    }
    closeAllOverlays();
  }

  /**
   * オーバーレイを開く
   * @param {string} name オーバーレイキー
   */
  function openOverlay(name) {
    closeAllOverlays();
    if (overlays[name]) {
      overlays[name].classList.add("active");
    }
  }

  /**
   * すべてのオーバーレイを閉じる
   */
  function closeAllOverlays() {
    Object.values(overlays).forEach((el) => el.classList.remove("active"));
  }

  /**
   * トーストメッセージを表示する
   * @param {string} message メッセージ
   */
  function showToast(message) {
    els.toast.textContent = message;
    els.toast.classList.add("active");
    clearTimeout(showToast._timer);
    showToast._timer = setTimeout(() => {
      els.toast.classList.remove("active");
    }, 2200);
  }

  /**
   * 比較処理の見た目（ローディング）をシミュレートする
   * @param {Function} done 完了後コールバック
   */
  function simulateCompare(done) {
    els.loading.classList.add("active");
    state.comparing = true;
    els.statusText.textContent = "比較中...";
    setTimeout(() => {
      els.loading.classList.remove("active");
      state.comparing = false;
      els.statusText.textContent = "比較完了";
      els.statusDiff.textContent = "差分 5 件（テキスト 2 / 画像 2 / 片側のみ 1）";
      els.statusDiff.classList.remove("ok");
      els.statusDiff.classList.add("diff");
      if (typeof done === "function") {
        done();
      }
    }, 900);
  }

  /**
   * パス表示を更新する
   */
  function refreshPaths() {
    const left = state.leftPath || "（未選択）";
    const right = state.rightPath || "（未選択）";

    els.leftPathStartup.textContent = left;
    els.rightPathStartup.textContent = right;
    els.leftPathMain.textContent = left;
    els.rightPathMain.textContent = right;

    els.leftPathStartup.classList.toggle("filled", !!state.leftPath);
    els.rightPathStartup.classList.toggle("filled", !!state.rightPath);
  }

  /**
   * 起動画面でファイルを選んだ体にする
   * @param {"left"|"right"} side 左右
   */
  function pickStartupFile(side) {
    if (side === "left") {
      state.leftPath = demoPaths.left;
    } else {
      state.rightPath = demoPaths.right;
    }
    refreshPaths();
    showToast((side === "left" ? "左" : "右") + "ファイルを選択しました（デモ）");
  }

  /**
   * 比較開始（起動画面 → メイン）
   */
  function startCompareFromStartup() {
    if (!state.leftPath || !state.rightPath) {
      showToast("左右両方の Excel ファイルを選択してください");
      return;
    }
    simulateCompare(() => {
      showScreen("main");
      showToast("比較結果を表示しました");
    });
  }

  /**
   * デモ用にサンプルを読み込んでメインへ
   */
  function loadDemo() {
    state.leftPath = demoPaths.left;
    state.rightPath = demoPaths.right;
    refreshPaths();
    simulateCompare(() => {
      showScreen("main");
      showToast("デモデータを読み込みました");
    });
  }

  /**
   * 再比較
   */
  function recompare() {
    if (!state.leftPath || !state.rightPath) {
      showToast("ファイルが選択されていません");
      return;
    }
    simulateCompare(() => {
      showToast("再比較が完了しました");
    });
  }

  /**
   * 片側差し替えダイアログを開く
   * @param {"left"|"right"} side 左右
   */
  function openReplace(side) {
    els.replaceSide.value = side;
    els.replacePath.value =
      side === "left" ? state.leftPath || demoPaths.left : state.rightPath || demoPaths.right;
    openOverlay("replace");
  }

  /**
   * 片側差し替えを適用する
   */
  function applyReplace() {
    const side = els.replaceSide.value;
    const path = els.replacePath.value.trim() || (side === "left" ? demoPaths.left : demoPaths.right);
    if (side === "left") {
      state.leftPath = path;
    } else {
      state.rightPath = path;
    }
    refreshPaths();
    closeAllOverlays();
    simulateCompare(() => {
      showToast((side === "left" ? "左" : "右") + "ファイルを差し替えて再比較しました");
    });
  }

  /**
   * 同期スクロールを設定する
   */
  function bindSyncScroll() {
    let lock = false;

    function sync(from, to) {
      if (!state.syncScroll || lock) {
        return;
      }
      lock = true;
      const ratio =
        from.scrollHeight <= from.clientHeight
          ? 0
          : from.scrollTop / (from.scrollHeight - from.clientHeight);
      to.scrollTop = ratio * (to.scrollHeight - to.clientHeight);
      updateMinimapFromScroll(from);
      lock = false;
    }

    els.leftViewport.addEventListener("scroll", () => sync(els.leftViewport, els.rightViewport));
    els.rightViewport.addEventListener("scroll", () => sync(els.rightViewport, els.leftViewport));
  }

  /**
   * スクロール位置から MiniMap の表示枠を更新する
   * @param {HTMLElement} viewport ビューポート
   */
  function updateMinimapFromScroll(viewport) {
    const max = viewport.scrollHeight - viewport.clientHeight;
    const ratio = max <= 0 ? 0 : viewport.scrollTop / max;
    const mapH = els.minimap.clientHeight;
    const vpH = els.minimapViewport.clientHeight;
    const top = ratio * (mapH - vpH);
    els.minimapViewport.style.top = Math.max(0, top) + "px";
  }

  /**
   * MiniMap のクリック／ドラッグで本体をスクロールする
   */
  function bindMinimap() {
    let dragging = false;

    function jumpTo(clientY) {
      const rect = els.minimap.getBoundingClientRect();
      const y = Math.min(Math.max(clientY - rect.top, 0), rect.height);
      const ratio = y / rect.height;
      const maxL = els.leftViewport.scrollHeight - els.leftViewport.clientHeight;
      const maxR = els.rightViewport.scrollHeight - els.rightViewport.clientHeight;
      els.leftViewport.scrollTop = ratio * maxL;
      els.rightViewport.scrollTop = ratio * maxR;
      updateMinimapFromScroll(els.leftViewport);
    }

    els.minimap.addEventListener("mousedown", (e) => {
      dragging = true;
      jumpTo(e.clientY);
      e.preventDefault();
    });

    window.addEventListener("mousemove", (e) => {
      if (dragging) {
        jumpTo(e.clientY);
      }
    });

    window.addEventListener("mouseup", () => {
      dragging = false;
    });
  }

  /**
   * 設定タブ切替
   */
  function bindSettingsTabs() {
    const buttons = document.querySelectorAll("[data-settings-tab]");
    const sections = document.querySelectorAll("[data-settings-section]");
    buttons.forEach((btn) => {
      btn.addEventListener("click", () => {
        const key = btn.getAttribute("data-settings-tab");
        buttons.forEach((b) => b.classList.toggle("active", b === btn));
        sections.forEach((s) => {
          s.classList.toggle("active", s.getAttribute("data-settings-section") === key);
        });
      });
    });
  }

  /**
   * イベントをバインドする
   */
  function bindEvents() {
    document.querySelectorAll("[data-action]").forEach((el) => {
      el.addEventListener("click", () => {
        const action = el.getAttribute("data-action");
        handleAction(action, el);
      });
    });

    document.querySelectorAll("[data-close-overlay]").forEach((el) => {
      el.addEventListener("click", () => closeAllOverlays());
    });

    overlays.sheetMap.addEventListener("click", (e) => {
      if (e.target === overlays.sheetMap) closeAllOverlays();
    });
    overlays.anchor.addEventListener("click", (e) => {
      if (e.target === overlays.anchor) closeAllOverlays();
    });
    overlays.replace.addEventListener("click", (e) => {
      if (e.target === overlays.replace) closeAllOverlays();
    });
    overlays.settings.addEventListener("click", (e) => {
      if (e.target === overlays.settings) closeAllOverlays();
    });
  }

  /**
   * data-action を処理する
   * @param {string} action アクション名
   * @param {HTMLElement} el 要素
   */
  function handleAction(action, el) {
    switch (action) {
      case "pick-left":
        pickStartupFile("left");
        break;
      case "pick-right":
        pickStartupFile("right");
        break;
      case "start-compare":
        startCompareFromStartup();
        break;
      case "load-demo":
        loadDemo();
        break;
      case "goto-startup":
        showScreen("startup");
        els.statusText.textContent = "ファイル選択待機";
        break;
      case "recompare":
        recompare();
        break;
      case "open-sheet-map":
        openOverlay("sheetMap");
        break;
      case "open-anchor":
        openOverlay("anchor");
        break;
      case "open-settings":
        openOverlay("settings");
        break;
      case "open-replace-left":
        openReplace("left");
        break;
      case "open-replace-right":
        openReplace("right");
        break;
      case "apply-sheet-map":
        closeAllOverlays();
        simulateCompare(() => showToast("シート対応を適用して再比較しました"));
        break;
      case "apply-anchor":
        closeAllOverlays();
        simulateCompare(() => showToast("アンカーを適用して再比較しました"));
        break;
      case "apply-replace":
        applyReplace();
        break;
      case "save-settings":
        closeAllOverlays();
        showToast("設定を YAML に保存しました（デモ）");
        break;
      case "toggle-sync":
        state.syncScroll = !state.syncScroll;
        el.classList.toggle("primary", state.syncScroll);
        showToast(state.syncScroll ? "同期スクロール: ON" : "同期スクロール: OFF");
        break;
      default:
        break;
    }
  }

  /**
   * 初期化
   */
  function init() {
    refreshPaths();
    bindEvents();
    bindSyncScroll();
    bindMinimap();
    bindSettingsTabs();
    showScreen("startup");
    updateMinimapFromScroll(els.leftViewport);
    els.statusText.textContent = "起動完了 — ファイルを選択してください";
  }

  document.addEventListener("DOMContentLoaded", init);
})();
