// 电子木鱼配置管理脚本（精简版 - 独立子页面）
'use strict';

// ========== 内嵌礼物数据（避免外部脚本路径问题） ==========
var douyinGiftList = [
  "小心心", "大啤酒", "棒棒糖", "玫瑰", "鲜花", "荧光棒", "粉丝团灯牌",
  "人气票", "你最好看", "加油鸭", "称心如意", "送你花花", "爱心", "比心",
  "鼓掌", "点赞", "墨镜", "钞票枪", "礼花筒", "欢乐礼花筒", "奇幻礼花筒",
  "告白气球", "玫瑰之约", "永生花", "告白花火", "爱的传音", "爱的华尔兹",
  "爱情静电", "甜蜜滋味", "海洋之心", "花海之恋", "花海予梦", "宇宙之心",
  "星际玫瑰", "星光女神", "蝶化蝶飞", "蝶·寄相思", "真爱永恒", "无尽浪漫",
  "真爱记忆", "繁花秘语", "守护臻爱", "情定三生", "跨时空之恋", "月下瀑布",
  "团团圆圆", "等待花开", "为你加冕", "晨光繁花", "繁花相赠", "深海奇缘",
  "跑车", "钻石飞机", "钻石火箭", "抖音1号", "为爱启航", "嘉年华", "梦幻城堡",
  "浪漫马车", "抖音飞艇", "云中秘境", "摩天大厦", "云霄大厦", "天空之境",
  "新春狂欢城", "梦回紫禁城", "金鳞化龙", "琴瑟和鸣", "凤凰来仪", "苍穹之翼",
  "御龙游侠", "永恒之钻", "钻石秘境", "神鹿传说", "古往今来", "荣耀之巅",
  "荣耀风冕", "骑士精神", "乘风破浪", "无畏守护", "月伴星辰", "七夕盛宴",
  "逐光寻月", "云上巡礼", "荣誉流星", "梦幻奇缘", "御见天下", "破晓·公爵",
  "战神·觉醒", "星云·公爵", "龙启盛世", "鹊梦千年", "星辰启岁",
  "鲲鹏万里", "火麒麟", "狂狮怒吼", "霁月苍狼", "龙腾四海", "至尊守护",
  "黄金守护者", "神鹰之恋", "金凤霓裳", "影驾临", "扬帆起航"
];

var weixinGiftList = [
  "点赞", "爱心", "玫瑰", "小花花", "棒棒糖", "啤酒", "咖啡", "奶茶",
  "掌声", "加油", "666", "比心", "飞吻", "气球", "荧光棒", "小灯笼",
  "粉丝牌", "人气票", "小星星", "小礼物", "小心意", "送你一朵花",
  "跑车", "直升机", "火箭", "热气球", "游艇", "飞机", "游轮", "梦幻城堡",
  "告白气球", "浪漫花海", "星辰大海", "月下情缘", "爱的告白", "甜蜜暴击",
  "心动瞬间", "璀璨星光", "流星雨", "彩虹桥", "音乐盒", "旋转木马",
  "幸运星", "招财猫", "锦鲤", "金元宝", "红包雨", "福袋",
  "嘉年华", "宇宙之心", "至尊礼炮", "龙腾四海", "凤舞九天", "麒麟献瑞",
  "万里长城", "帝王冠冕", "荣耀殿堂", "王者权杖", "天使之翼", "海神之戟",
  "独角兽", "凤凰于飞", "一生所爱", "永恒誓约", "传世典藏", "江山如画",
  "星耀全场", "巅峰时刻", "君临天下", "盛世华章", "璀璨王冠",
  "中秋月饼", "端午粽子", "新春红包", "元宵花灯", "七夕鹊桥", "圣诞麋鹿",
  "跨年烟花", "新年福字", "平安果", "情人节礼盒", "母亲节康乃馨"
];

// ========== 全局状态 ==========
let currentPlatform = 'douyin';
let configs = { douyin: null, weixin: null };
let liveState = {
  douyin: { total: 0, likes: 0, gifts: 0 },
  weixin: { total: 0, likes: 0, gifts: 0 }
};
// 记录打开的显示窗口引用
let displayWindows = { douyin: null, weixin: null };

async function init() {
  setupPlatformTabs();
  setupEventListeners();
  await loadConfigs();
  populateAllGifts();
  loadPlatformConfig('douyin');
  loadPlatformConfig('weixin');
  setupLivePreview('douyin');
  setupLivePreview('weixin');
}

// 平台 Tab 切换
function setupPlatformTabs() {
  document.querySelectorAll('.tab-btn').forEach(tab => {
    tab.addEventListener('click', () => {
      const platform = tab.dataset.platform;
      document.querySelectorAll('.tab-btn').forEach(t => t.classList.remove('active'));
      tab.classList.add('active');
      document.querySelectorAll('.platform-page').forEach(p => p.classList.remove('active'));
      document.getElementById('page-' + platform).classList.add('active');
      currentPlatform = platform;
    });
  });
}

// 填充两个平台的礼物列表
function populateAllGifts() {
  populateGiftSelect('douyin');
  populateGiftSelect('weixin');
}

function populateGiftSelect(platform) {
  const select = document.getElementById(platform + '-gift-selector');
  if (!select) return;
  select.innerHTML = '';

  // 使用内嵌的礼物数据
  var gifts = platform === 'douyin' ? douyinGiftList : weixinGiftList;

  gifts.forEach(function(name) {
    var opt = document.createElement('option');
    opt.value = name;
    opt.textContent = name;
    select.appendChild(opt);
  });
}

// 默认配置模板
function getDefaultConfig() {
  return {
    woodfish: { customImage: '', enableCustomImage: false },
    triggers: {
      gift: { enabled: true, selectedGifts: [], multiplier: 1 },
      danmaku: { enabled: true },
      like: { enabled: true, multiplier: 1 }
    },
    display: { meritText: '上岸机会', meritTextColor: '#ffd700', backgroundColor: '#54d100', scrollTextColor: '#ffffff' },
    giftList: []
  };
}

// 加载配置 - 优先 localStorage，其次 opener，最后默认值
async function loadConfigs() {
  // 1. 尝试从 localStorage 加载
  try {
    var saved = localStorage.getItem('muyu-config-douyin');
    if (saved) configs.douyin = JSON.parse(saved);
  } catch (e) {}
  try {
    var saved2 = localStorage.getItem('muyu-config-weixin');
    if (saved2) configs.weixin = JSON.parse(saved2);
  } catch (e) {}

  // 2. 尝试从 opener（主窗口）获取
  if (!configs.douyin && window.opener && window.opener.muyuState) {
    try { configs.douyin = window.opener.muyuState.configs.douyin; } catch (e) {}
  }
  if (!configs.weixin && window.opener && window.opener.muyuState) {
    try { configs.weixin = window.opener.muyuState.configs.weixin; } catch (e) {}
  }

  // 3. 尝试 fetch（可能在某些环境下可用）
  if (!configs.douyin) {
    try {
      var res = await fetch('../../configs/douyin-muyu.json');
      if (res.ok) configs.douyin = await res.json();
    } catch (e) {}
  }
  if (!configs.weixin) {
    try {
      var res2 = await fetch('../../configs/shipinhao-muyu.json');
      if (res2.ok) configs.weixin = await res2.json();
    } catch (e) {}
  }

  // 4. 使用默认配置
  if (!configs.douyin) configs.douyin = getDefaultConfig();
  if (!configs.weixin) configs.weixin = getDefaultConfig();

  console.log('[木鱼配置] 配置加载完成', configs);
}

// 加载平台配置到对应子页面
function loadPlatformConfig(platform) {
  const config = configs[platform];
  if (!config) return;
  const p = platform;

  // 图片
  const imgName = document.getElementById(p + '-selected-image-name');
  if (config.woodfish && config.woodfish.customImage) {
    imgName.textContent = config.woodfish.customImage.startsWith('data:') ? '已选择自定义图片' : config.woodfish.customImage.split('/').pop();
  } else {
    imgName.textContent = '使用默认木鱼图片';
  }

  // 触发设置
  if (config.triggers) {
    if (config.triggers.gift) document.getElementById(p + '-trigger-gift').checked = config.triggers.gift.enabled !== false;
    if (config.triggers.danmaku) document.getElementById(p + '-trigger-danmaku').checked = config.triggers.danmaku.enabled !== false;
    if (config.triggers.like) document.getElementById(p + '-trigger-like').checked = config.triggers.like.enabled !== false;

    // 礼物选中状态
    const select = document.getElementById(p + '-gift-selector');
    const selected = (config.triggers.gift && config.triggers.gift.selectedGifts) || [];
    Array.from(select.options).forEach(opt => {
      opt.selected = selected.includes(opt.value);
    });

    // 倍数
    if (config.triggers.gift) document.getElementById(p + '-gift-multiplier').value = config.triggers.gift.multiplier || 1;
    if (config.triggers.like) document.getElementById(p + '-like-multiplier').value = config.triggers.like.multiplier || 1;
  }

  // 显示
  if (config.display) {
    document.getElementById(p + '-merit-text').value = config.display.meritText || '上岸机会';
    document.getElementById(p + '-text-color').value = config.display.meritTextColor || '#ffd700';
    document.getElementById(p + '-bg-color').value = config.display.backgroundColor || '#54d100';
  }
  updateColorValues(p);
  updateLivePreviewStyle(p);
}

// 更新颜色值显示
function updateColorValues(platform) {
  document.getElementById(platform + '-text-color-value').textContent = document.getElementById(platform + '-text-color').value;
  document.getElementById(platform + '-bg-color-value').textContent = document.getElementById(platform + '-bg-color').value;
}

// 更新实时预览样式
function updateLivePreviewStyle(platform) {
  var textColor = document.getElementById(platform + '-text-color').value;
  var bgColor = document.getElementById(platform + '-bg-color').value;
  var preview = document.getElementById(platform + '-live-preview');
  if (preview) {
    preview.style.backgroundColor = bgColor;
    var counter = document.getElementById(platform + '-live-counter');
    if (counter) counter.style.color = textColor;
  }
}

// 实时预览 - 点击木鱼
function setupLivePreview(platform) {
  const img = document.getElementById(platform + '-live-muyu-img');
  if (!img) return;
  img.addEventListener('click', () => {
    liveState[platform].total++;
    img.classList.remove('hit');
    void img.offsetWidth;
    img.classList.add('hit');
    showFloat(platform);
    updateLiveStats(platform);
  });
}

function showFloat(platform) {
  const container = document.getElementById(platform + '-float-container');
  const meritText = document.getElementById(platform + '-merit-text').value || '功德';
  const textColor = document.getElementById(platform + '-text-color').value;
  const el = document.createElement('div');
  el.className = 'float-text';
  el.textContent = meritText + ' +1';
  el.style.color = textColor;
  el.style.left = (50 + (Math.random() - 0.5) * 40) + '%';
  el.style.transform = 'translateX(-50%)';
  container.appendChild(el);
  setTimeout(() => el.remove(), 1200);
}

function updateLiveStats(platform) {
  var s = liveState[platform];
  var meritText = document.getElementById(platform + '-merit-text').value || '上岸机会';
  var counter = document.getElementById(platform + '-live-counter');
  if (counter) counter.textContent = meritText + ' ' + s.total;

  // 同步到 localStorage，让 display 窗口也能读取
  localStorage.setItem('muyu-total-' + platform, s.total);

  // 如果 display 窗口已打开，直接 postMessage
  var win = displayWindows[platform];
  if (win && !win.closed) {
    try {
      win.postMessage({ type: 'muyu-trigger', platform: platform, hits: 1, source: '手动' }, '*');
    } catch (e) {}
  }
}

// 事件监听
function setupEventListeners() {
  ['douyin', 'weixin'].forEach(p => {
    document.getElementById(p + '-image-file-input').addEventListener('change', (e) => handleImageFileSelect(e, p));
    document.getElementById(p + '-text-color').addEventListener('input', () => { updateColorValues(p); updateLivePreviewStyle(p); });
    document.getElementById(p + '-bg-color').addEventListener('input', () => { updateColorValues(p); updateLivePreviewStyle(p); });
  });

  document.getElementById('btn-open-douyin-display').addEventListener('click', () => openBrowserDisplay('douyin'));
  document.getElementById('btn-open-weixin-display').addEventListener('click', () => openBrowserDisplay('weixin'));

  // 透明叠加窗口按钮 - 通过 localStorage 通知主窗口打开
  document.getElementById('btn-open-weixin-overlay').addEventListener('click', () => {
    localStorage.setItem('open-muyu-overlay', JSON.stringify({ platform: 'weixin', ts: Date.now() }));
    showNotification('正在打开透明叠加窗口...', 'success');
  });
}

// 处理图片选择
function handleImageFileSelect(event, platform) {
  const file = event.target.files[0];
  if (!file) return;
  const validTypes = ['image/png', 'image/jpeg', 'image/jpg', 'image/gif', 'image/webp', 'image/svg+xml'];
  if (!validTypes.includes(file.type)) {
    showNotification('请选择有效的图片文件', 'error');
    return;
  }
  const reader = new FileReader();
  reader.onload = function(e) {
    const config = configs[platform];
    if (config) {
      config.woodfish.customImage = e.target.result;
      config.woodfish.enableCustomImage = true;
    }
    document.getElementById(platform + '-selected-image-name').textContent = file.name;
    const liveImg = document.getElementById(platform + '-live-muyu-img');
    if (liveImg) liveImg.src = e.target.result;
    showNotification('图片已选择', 'success');
  };
  reader.readAsDataURL(file);
}

// 打开浏览器显示窗口
function openBrowserDisplay(platform) {
  const width = 600, height = 500;
  const left = (screen.width - width) / 2;
  const top = (screen.height - height) / 2;
  const url = 'muyu-display.html?platform=' + platform;
  const features = 'width=' + width + ',height=' + height + ',left=' + left + ',top=' + top + ',resizable=yes';
  const win = window.open(url, 'MuyuDisplay_' + platform, features);
  if (win) {
    displayWindows[platform] = win;
    showNotification((platform === 'douyin' ? '抖音' : '视频号') + '浏览器显示已打开', 'success');
  } else {
    showNotification('无法打开窗口，请检查浏览器设置', 'error');
  }
}

// 获取选中的礼物
function getSelectedGifts(platform) {
  const select = document.getElementById(platform + '-gift-selector');
  return Array.from(select.selectedOptions).map(o => o.value);
}

// 保存配置
function saveConfig(platform) {
  var config = configs[platform];
  if (!config) config = configs[platform] = getDefaultConfig();
  var p = platform;

  // 确保结构完整
  if (!config.triggers) config.triggers = {};
  if (!config.triggers.gift) config.triggers.gift = {};
  if (!config.triggers.danmaku) config.triggers.danmaku = {};
  if (!config.triggers.like) config.triggers.like = {};
  if (!config.display) config.display = {};
  if (!config.woodfish) config.woodfish = {};

  config.triggers.gift.enabled = document.getElementById(p + '-trigger-gift').checked;
  config.triggers.danmaku.enabled = document.getElementById(p + '-trigger-danmaku').checked;
  config.triggers.like.enabled = document.getElementById(p + '-trigger-like').checked;
  config.triggers.gift.selectedGifts = getSelectedGifts(p);
  config.triggers.gift.multiplier = parseFloat(document.getElementById(p + '-gift-multiplier').value) || 1;
  config.triggers.like.multiplier = parseFloat(document.getElementById(p + '-like-multiplier').value) || 1;
  config.display.meritText = document.getElementById(p + '-merit-text').value || '功德';
  config.display.meritTextColor = document.getElementById(p + '-text-color').value;
  config.display.backgroundColor = document.getElementById(p + '-bg-color').value;

  localStorage.setItem('muyu-config-' + platform, JSON.stringify(config));
  localStorage.setItem('muyu-config-ver-' + platform, Date.now().toString());
  showNotification('配置已保存', 'success');
}

// 应用配置 - 同步到显示窗口
function applyConfig(platform) {
  saveConfig(platform);

  // 发送配置到已打开的显示窗口
  var win = displayWindows[platform];
  if (win && !win.closed) {
    try {
      win.postMessage({ type: 'muyu-config-update', platform: platform, config: configs[platform] }, '*');
      showNotification('配置已应用到显示窗口', 'success');
    } catch (e) {
      showNotification('配置已保存，显示窗口将自动同步', 'info');
    }
  } else {
    // 也通知主窗口
    if (window.opener && !window.opener.closed) {
      window.opener.postMessage({ type: 'muyu-config-update', platform: platform, config: configs[platform] }, '*');
    }
    showNotification('配置已保存', 'info');
  }
}

// 重置配置
async function resetConfig(platform) {
  if (!confirm('确定要重置为默认配置吗？')) return;
  liveState[platform] = { total: 0, likes: 0, gifts: 0 };
  updateLiveStats(platform);
  await loadConfigs();
  populateGiftSelect(platform);
  loadPlatformConfig(platform);
  localStorage.removeItem('muyu-config-' + platform);
  showNotification('已重置为默认配置', 'info');
}

// 显示通知
function showNotification(message, type) {
  type = type || 'info';
  var colors = { success: '#00c853', error: '#ff1744', warning: '#ffd600', info: '#2196f3' };
  var el = document.createElement('div');
  el.style.cssText = 'position:fixed;top:20px;right:20px;background:' + colors[type] + ';color:white;padding:12px 20px;border-radius:8px;box-shadow:0 4px 12px rgba(0,0,0,0.3);z-index:10000;animation:slideIn .3s ease;font-size:14px;';
  el.textContent = message;
  document.body.appendChild(el);
  setTimeout(function() { el.style.animation = 'slideOut .3s ease'; setTimeout(function() { el.remove(); }, 300); }, 2500);
}

// 初始化
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init);
} else {
  init();
}

// 动画样式
var style = document.createElement('style');
style.textContent = '@keyframes slideIn{from{transform:translateX(100%);opacity:0}to{transform:translateX(0);opacity:1}}@keyframes slideOut{from{transform:translateX(0);opacity:1}to{transform:translateX(100%);opacity:0}}';
document.head.appendChild(style);
