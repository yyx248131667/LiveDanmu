'use strict';

const MAX_MESSAGES = 500;
const CONNECT_TIMEOUT_MS = 20000;
const DOUYIN_GIFT_CATALOG = [
  ['小心心', 1], ['玫瑰', 1], ['粉丝团灯牌', 1], ['人气票', 1], ['樱花', 1], ['抱拳了铁子', 1],
  ['你最好看', 2], ['板砖', 5], ['爱心', 5], ['甜狗', 6], ['棒棒糖', 9], ['荧光棒', 10], ['mojito', 10],
  ['撩一下', 19], ['小云朵', 50], ['炸酱面', 66], ['墨镜', 99], ['亲吻', 99], ['快乐水', 99], ['Thuglife', 99], ['啤酒', 99],
  ['仙女棒', 166], ['礼花筒', 199], ['定情钻戒', 199], ['比心', 199], ['大金链', 299], ['锦鲤', 399],
  ['热气球', 520], ['表白气球', 520], ['浪漫花火', 599], ['桃花签', 666], ['保时捷', 1200],
  ['红墙白雪', 1888], ['炫彩射击', 1888], ['单车恋人', 1899], ['浪漫恋人', 1999], ['开运醒狮', 2024],
  ['龙珠纳福', 2388], ['奇幻八音盒', 2399], ['花海泛舟', 2800], ['火箭', 2888], ['带你躺赢', 2990],
  ['传送门', 2999], ['直升机', 2999], ['庆典趴', 2999], ['私人飞机', 3000], ['冰冻战车', 3000],
  ['薰衣草庄园', 3300], ['奏响人生', 3666], ['游艇', 3888], ['老铁', 3999], ['海上生明月', 4166],
  ['心动丘比特', 4321], ['真情萌动', 4433], ['星河相望', 4520], ['奇幻花潮', 4520], ['福佑万家', 4888],
  ['壁上飞仙', 4999], ['火龙爆发', 5000], ['华灯初上', 5000], ['兰博基尼', 5200], ['豪华邮轮', 6000],
  ['豪华火箭', 6666], ['宇宙飞船', 9999], ['都市游侠', 10000], ['战神觉醒', 10000], ['抖音 1 号', 10001],
  ['破晓公爵', 11999], ['火山爆发', 13140], ['钻石秘境', 16000], ['宇宙之心', 18888], ['守护臻爱', 18888],
  ['忆往昔', 18888], ['青莲剑仙', 19999], ['星辰启岁', 20000], ['敦煌守护神', 20000], ['极光之约', 20000],
  ['天下云长', 20000], ['七彩玫瑰', 21000], ['浪漫马车', 28888], ['嘉年华', 30000], ['臻爱降临', 30000],
  ['御龙游侠', 30000], ['战神守护', 30000], ['敦煌惊梦', 30000], ['幸运公爵', 30000], ['至尊礼炮', 66666]
];
const WEIXIN_GIFT_CATALOG = [
  ['爱心', 1, '1'],
  ['鼓掌', 10, '2'], ['喝彩', 10], ['能量饮料', 10],
  ['小花', 50, '3'], ['太阳镜', 99],
  ['奶茶', 166, '4'], ['倒杯茶', 188], ['玫瑰花', 500],
  ['打 call', 999, '5'], ['文房四宝', 999],
  ['烟花弹', 2000, '6'], ['告白气球', 5200], ['火箭', 6666],
  ['梦幻城堡', 10000], ['一箭钟情', 15000], ['摘星星', 20000], ['桃花岛', 30000, '7']
];
const state = { status: 'idle', filter: 'all', messages: [], viewers: 0, paused: false, drumPlatform: 'douyin', drumConfigs: {}, drumImages: {}, gifts: { douyin: new Map(), weixin: new Map() }, platformStatus: { douyin: 'idle', weixin: 'idle' } };
const $ = (selector) => document.querySelector(selector);
const list = $('#message-list');
const empty = $('#empty-state');
const urlInput = $('#room-url');
const weixinInput = $('#weixin-room');
const connectButton = $('#connect-button');
const disconnectButton = $('#disconnect-button');
const formMessage = $('#form-message');
const statusDot = $('#status-dot');
const statusText = $('#status-text');
let toastTimer;
let connectTimer;

function post(message) {
  if (!window.chrome?.webview) {
    toast('桌面通信组件尚未就绪，请重新打开应用');
    return false;
  }
  window.chrome.webview.postMessage(JSON.stringify(message));
  return true;
}

function toast(message) {
  const element = $('#toast');
  element.textContent = message;
  element.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => element.classList.remove('show'), 2600);
}

function setStatus(status, text) {
  state.status = status;
  statusDot.className = `status-dot ${status}`;
  statusText.textContent = text;
  connectButton.disabled = ['connecting', 'reconnecting', 'connected'].includes(status);
  connectButton.textContent = status === 'connecting' ? '正在连接…' : status === 'reconnecting' ? '正在重连…' : '连接直播间';
  disconnectButton.disabled = status === 'idle' || status === 'error';
  // Keep both room fields editable after either platform logs in or connects.
  // Only the connection action itself is locked while a session is active.
  urlInput.disabled = false;
  weixinInput.disabled = false;
  if (status !== 'connecting') clearTimeout(connectTimer);
}

function showFormMessage(text, isError = false) {
  formMessage.textContent = text;
  formMessage.classList.toggle('error', isError);
}

function normalizeRoomUrl(value) {
  const trimmed = value.trim();
  if (/^\d{3,}$/.test(trimmed)) return `https://live.douyin.com/${trimmed}`;
  try {
    const url = new URL(trimmed);
    if (url.protocol === 'https:' && url.hostname === 'live.douyin.com' && /^\/\d+/.test(url.pathname)) return url.href;
  } catch { /* validation below handles it */ }
  return null;
}

function normalizeType(type = '') {
  if (type === 'danmaku' || type === 'chat') return 'chat';
  if (type === 'enter' || type === 'member' || type === 'social' || type === 'follow' || type === 'system') return 'member';
  return ['gift', 'like'].includes(type) ? type : 'chat';
}

function typeLabel(type) {
  return { chat: '弹幕', gift: '礼物', like: '点赞', member: '进场' }[type] || '消息';
}

function updateMetrics() {
  $('#message-count').textContent = String(state.messages.length);
  $('#gift-count').textContent = String(state.messages.filter((item) => item.type === 'gift').length);
}

function addMessage(data) {
  const type = normalizeType(data.msgType || data.msg_type || data.method);
  const message = {
    type,
    user: data.user || data.username || data.nickname || '匿名观众',
    content: data.content || data.message || typeLabel(type),
    time: new Date()
  };
  state.messages.push(message);
  if (state.messages.length > MAX_MESSAGES) {
    state.messages.shift();
    list.querySelector('.message')?.remove();
  }

  empty.hidden = true;
  const row = document.createElement('article');
  row.className = `message ${type}`;
  row.dataset.type = type;
  row.hidden = state.filter !== 'all' && state.filter !== type;
  row.innerHTML = '<span class="kind"></span><span class="user"></span><span class="content"></span><time class="time"></time>';
  row.querySelector('.kind').textContent = typeLabel(type);
  row.querySelector('.user').textContent = message.user;
  row.querySelector('.content').textContent = message.content;
  row.querySelector('.time').textContent = message.time.toLocaleTimeString('zh-CN', { hour12: false });
  list.appendChild(row);
  updateMetrics();
  if (!state.paused) list.scrollTop = list.scrollHeight;
}

function interpretBackendStatus(data) {
  const text = data.text || '';
  if (data.status === 'connected') return ['connected', text || '抖音已连接'];
  if (/重连|断开.*后/.test(text)) return ['reconnecting', text];
  if (/失败|结束|需要登录/.test(text)) return ['error', text];
  if (/正在|初始化|启动/.test(text)) return ['connecting', text];
  return ['idle', text || '尚未连接'];
}

function handleBackendMessage(event) {
  let data = event?.data ?? event;
  if (typeof data === 'string') {
    try { data = JSON.parse(data); } catch { return; }
  }
  if (!data) return;
  if (data.type === 'danmaku') addMessage(data);
  if (data.type === 'gift-collected' && data.giftName) {
    const platform = String(data.platform).includes('weixin') ? 'weixin' : 'douyin';
    state.gifts[platform].set(String(data.giftId || data.giftName), data.giftName);
    if (platform === state.drumPlatform) refreshGiftOptions();
  }
  if (data.type === 'muyu-config' && data.platform && data.config) {
    state.drumConfigs[data.platform] = data.config;
    if (data.platform === state.drumPlatform) applyDrumConfig(data.config);
  }
  if (data.type === 'viewer-count' && data.platform === 'douyin') {
    state.viewers = Number(data.count) || 0;
    $('#viewer-count').textContent = state.viewers.toLocaleString('zh-CN');
    if (state.messages.length === 0) {
      empty.querySelector('h3').textContent = '直播数据已接入';
      empty.querySelector('p').textContent = `当前在线 ${state.viewers.toLocaleString('zh-CN')}，正在等待弹幕、礼物或点赞。`;
    }
  }
  if (data.type === 'viewer-count' && String(data.platform).includes('weixin')) {
    state.viewers = Number(data.count) || 0;
    $('#viewer-count').textContent = state.viewers.toLocaleString('zh-CN');
  }
  if (data.type === 'ws-status' && ['douyin', 'weixin'].includes(data.platform)) {
    const [status, text] = interpretBackendStatus(data);
    state.platformStatus[data.platform] = status;
    const connectedPlatforms = Object.values(state.platformStatus).filter((value) => value === 'connected').length;
    const aggregateStatus = connectedPlatforms ? 'connected' : status;
    setStatus(aggregateStatus, connectedPlatforms === 2 ? '双平台已连接' : text);
    $('#platform-status').textContent = `抖音 ${state.platformStatus.douyin === 'connected' ? '已连接' : '未连接'} / 视频号 ${state.platformStatus.weixin === 'connected' ? '已连接' : '未连接'}`;
    showFormMessage(status === 'connected' ? '连接成功，正在接收实时互动。' : text, status === 'error');
    if (status === 'error') toast(text);
  }
  if (data.type === 'error') {
    const message = data.message || '连接失败，请检查直播间地址和网络。';
    setStatus('error', '连接失败');
    showFormMessage(message, true);
    toast(message);
  }
}

$('#connect-form').addEventListener('submit', (event) => {
  event.preventDefault();
  document.querySelector('[data-filter="all"]')?.click();
  const douyinValue = urlInput.value.trim();
  const weixinValue = weixinInput.value.trim();
  const normalizedUrl = douyinValue ? normalizeRoomUrl(douyinValue) : '';
  if (!douyinValue && !weixinValue) {
    showFormMessage('请至少填写一个直播平台。', true);
    return;
  }
  if (douyinValue && !normalizedUrl) {
    urlInput.classList.add('invalid');
    showFormMessage('请输入有效的抖音直播间完整地址或数字房间号。', true);
    return;
  }
  if (normalizedUrl) urlInput.value = normalizedUrl;
  urlInput.classList.remove('invalid');
  showFormMessage('正在建立直播间连接…');
  localStorage.setItem('douyin-room-url', normalizedUrl || '');
  localStorage.setItem('weixin-room-id', weixinValue);
  setStatus('connecting', '正在连接');
  if (!post({ type: 'connect', douyinUrl: normalizedUrl || '', weixinUrl: weixinValue })) {
    setStatus('error', '通信组件不可用');
    return;
  }
  clearTimeout(connectTimer);
  connectTimer = setTimeout(() => {
    if (state.status !== 'connecting') return;
    setStatus('error', '连接超时');
    showFormMessage('连接等待超过 20 秒，请确认直播间正在开播后重试。', true);
  }, CONNECT_TIMEOUT_MS);
});

disconnectButton.addEventListener('click', () => {
  post({ type: 'disconnect' });
  setStatus('idle', '尚未连接');
  showFormMessage('已断开连接，可修改地址后重新连接。');
});

$('#login-button').addEventListener('click', () => post({ type: 'openDouyinLogin' }));
$('#weixin-login-button').addEventListener('click', () => post({ type: 'openWeixinLogin' }));
$('#pop-danmaku-button').addEventListener('click', () => post({ type: 'popDanmaku' }));
$('#show-drum-settings').addEventListener('click', () => {
  $('#stream-content').hidden = true;
  $('#drum-settings-view').hidden = false;
  $('#show-drum-settings').classList.add('active');
  post({ type: 'switch-platform', platform: state.drumPlatform });
});
$('#close-drum-settings').addEventListener('click', () => {
  $('#drum-settings-view').hidden = true;
  $('#stream-content').hidden = false;
  $('#show-drum-settings').classList.remove('active');
});

$('#page-opacity').addEventListener('input', (event) => {
  const value = Number(event.currentTarget.value);
  $('#opacity-value').textContent = `${value}%`;
  post({ type: 'opacity', value: value / 100 });
});

$('#save-drum-button').addEventListener('click', () => {
  const text = $('#drum-text').value.trim() || '功德';
  const giftRate = Math.max(1, Math.min(50, Number($('#gift-rate').value) || 1));
  const bubbleText = $('#bubble-text').value.trim() || '{用户}{文本}';
  const platform = state.drumPlatform;
  const drumImageData = state.drumImages[platform] || state.drumConfigs[platform]?.customSkinData || null;
  const config = {
      text, skin: drumImageData ? 'custom' : 'muyu', customSkinData: drumImageData, greenScreen: false, soundEnabled: true,
      triggerDanmaku: $('#trigger-chat').checked, triggerGift: $('#trigger-gift').checked, triggerLike: $('#trigger-like').checked,
      triggerEnter: false, triggerFollow: false, likeRate: 1, enterRate: 1, followRate: 1,
      giftRate, giftSelect: $('#gift-select').value,
      giftRules: { '其他': giftRate }, danmakuRules: { '其他': 1 }, bubbleText,
      textColor: $('#text-color').value, numColor: $('#number-color').value,
      textLayer: document.querySelector('[name="text-layer"]:checked').value,
      giftMode: document.querySelector('[name="gift-mode"]:checked').value,
      audioSpeed: 100, volume: 80, mute: false
  };
  state.drumConfigs[platform] = config;
  post({ type: 'save-config', platform, config });
  toast(`${platform === 'douyin' ? '抖音' : '视频号'}敲鼓设置已保存`);
});

function changeGiftRate(delta) {
  const input = $('#gift-rate');
  input.value = String(Math.max(1, Math.min(50, (Number(input.value) || 1) + delta)));
}
$('#gift-rate-minus').addEventListener('click', () => changeGiftRate(-1));
$('#gift-rate-plus').addEventListener('click', () => changeGiftRate(1));
document.querySelectorAll('[data-token]').forEach((button) => button.addEventListener('click', () => {
  const input = $('#bubble-text');
  const start = input.selectionStart ?? input.value.length;
  input.value = input.value.slice(0, start) + button.dataset.token + input.value.slice(input.selectionEnd ?? start);
  input.focus();
  input.setSelectionRange(start + button.dataset.token.length, start + button.dataset.token.length);
}));

$('#drum-image').addEventListener('change', (event) => {
  const file = event.currentTarget.files?.[0];
  if (!file) return;
  if (file.size > 3 * 1024 * 1024) return toast('图片不能超过 3 MB');
  const reader = new FileReader();
  reader.onload = () => {
    const platform = state.drumPlatform;
    state.drumImages[platform] = reader.result;
    post({ type: 'upload-skin', platform, skinType: 'custom', imageData: reader.result });
    toast(`${platform === 'douyin' ? '抖音' : '视频号'}鼓图片已保存`);
  };
  reader.readAsDataURL(file);
});

$('#open-drum-button').addEventListener('click', () => {
  post({ type: 'openMuyuOverlay', platform: state.drumPlatform });
});

function refreshGiftOptions() {
  const select = $('#gift-select');
  const selected = state.drumConfigs[state.drumPlatform]?.giftSelect || select.value || 'all';
  const defaults = state.drumPlatform === 'douyin'
    ? [['all', '全部礼物'], ...DOUYIN_GIFT_CATALOG.map(([name, coins]) => [name, `${name} · ${coins.toLocaleString('zh-CN')} 抖币`])]
    : [['all', '全部礼物'], ...WEIXIN_GIFT_CATALOG.map(([name, beans]) => [name, `${name} · ${beans.toLocaleString('zh-CN')} 微信豆`])];
  const options = new Map(defaults);
  state.gifts[state.drumPlatform].forEach((name, id) => options.set(id, name));
  select.innerHTML = '';
  options.forEach((name, id) => select.add(new Option(name, id)));
  select.value = options.has(selected) ? selected : 'all';
}

function applyDrumConfig(config = {}) {
  $('#trigger-gift').checked = config.triggerGift ?? true;
  $('#trigger-chat').checked = config.triggerDanmaku ?? false;
  $('#trigger-like').checked = config.triggerLike ?? false;
  $('#gift-rate').value = String(config.giftRate || 1);
  $('#drum-text').value = config.text || '功德';
  $('#bubble-text').value = config.bubbleText || '{用户}{文本}';
  $('#text-color').value = config.textColor || '#ffffff';
  $('#number-color').value = config.numColor || '#d65a4a';
  document.querySelector(`[name="text-layer"][value="${config.textLayer || 'above'}"]`).checked = true;
  document.querySelector(`[name="gift-mode"][value="${config.giftMode || 'separate'}"]`).checked = true;
  refreshGiftOptions();
}

document.querySelectorAll('[data-drum-platform]').forEach((button) => button.addEventListener('click', () => {
  state.drumPlatform = button.dataset.drumPlatform;
  document.querySelector('.platform-tab.active')?.classList.remove('active');
  button.classList.add('active');
  applyDrumConfig(state.drumConfigs[state.drumPlatform]);
  post({ type: 'switch-platform', platform: state.drumPlatform });
}));

document.querySelectorAll('.filter').forEach((button) => {
  button.addEventListener('click', () => {
    document.querySelector('.filter.active')?.classList.remove('active');
    button.classList.add('active');
    state.filter = button.dataset.filter;
    localStorage.setItem('message-filter', state.filter);
    list.querySelectorAll('.message').forEach((row) => {
      row.hidden = state.filter !== 'all' && row.dataset.type !== state.filter;
    });
  });
});

$('#pause-button').addEventListener('click', (event) => {
  state.paused = !state.paused;
  event.currentTarget.classList.toggle('active', state.paused);
  event.currentTarget.textContent = state.paused ? '继续滚动' : '暂停滚动';
  if (!state.paused) list.scrollTop = list.scrollHeight;
});

$('#clear-button').addEventListener('click', () => {
  state.messages = [];
  list.querySelectorAll('.message').forEach((item) => item.remove());
  empty.hidden = false;
  updateMetrics();
  toast('消息列表已清空');
});

$('#export-button').addEventListener('click', () => {
  if (!state.messages.length) return toast('当前没有可导出的消息');
  const safeCell = (value) => String(value).replace(/[\t\r\n]+/g, ' ');
  const lines = ['时间\t类型\t用户\t内容', ...state.messages.map((item) =>
    `${item.time.toLocaleString('zh-CN')}\t${typeLabel(item.type)}\t${safeCell(item.user)}\t${safeCell(item.content)}`)];
  const blob = new Blob(['\ufeff' + lines.join('\n')], { type: 'text/tab-separated-values;charset=utf-8' });
  const anchor = document.createElement('a');
  anchor.href = URL.createObjectURL(blob);
  anchor.download = `直播弹幕_${new Date().toISOString().slice(0, 10)}.tsv`;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(anchor.href), 0);
});

document.querySelectorAll('[data-window]').forEach((button) => {
  button.addEventListener('click', () => post({ type: button.dataset.window }));
});

$('#titlebar').addEventListener('dblclick', (event) => {
  if (!event.target.closest('button')) post({ type: 'maximize' });
});

let dragPoint;
$('#titlebar').addEventListener('pointerdown', (event) => {
  if (event.target.closest('button')) return;
  dragPoint = { x: event.screenX, y: event.screenY };
  event.currentTarget.setPointerCapture(event.pointerId);
});
$('#titlebar').addEventListener('pointermove', (event) => {
  if (!dragPoint || !event.buttons) return;
  const dx = event.screenX - dragPoint.x;
  const dy = event.screenY - dragPoint.y;
  dragPoint = { x: event.screenX, y: event.screenY };
  if (dx || dy) post({ type: 'drag-move', dx, dy });
});
$('#titlebar').addEventListener('pointerup', () => { dragPoint = null; });
$('#titlebar').addEventListener('pointercancel', () => { dragPoint = null; });

window.onHostMessage = handleBackendMessage;
if (window.chrome?.webview) window.chrome.webview.addEventListener('message', handleBackendMessage);
urlInput.value = localStorage.getItem('douyin-room-url') || '';
weixinInput.value = localStorage.getItem('weixin-room-id') || '';
const savedFilter = localStorage.getItem('message-filter');
if (savedFilter && document.querySelector(`[data-filter="${savedFilter}"]`)) document.querySelector(`[data-filter="${savedFilter}"]`).click();
setStatus('idle', '尚未连接');
