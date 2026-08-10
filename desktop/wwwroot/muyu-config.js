'use strict';

// ========== 内嵌礼物数据 ==========
var douyinGifts = [
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
var wechatVideoGifts = [
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

let currentPlatform = 'douyin';
let configs = { douyin: null, weixin: null };
let liveState = { total: 0, likes: 0, gifts: 0 };

async function init() {
  setupPlatformTabs();
  setupColorPickers();
  setupEventListeners();
  setupLivePreview();
  await loadConfigs();
  switchPlatform('douyin');
}

function setupPlatformTabs() {
  document.querySelectorAll('.tab-btn').forEach(tab => {
    tab.addEventListener('click', () => {
      document.querySelectorAll('.tab-btn').forEach(t => t.classList.remove('active'));
      tab.classList.add('active');
      switchPlatform(tab.dataset.platform);
    });
  });
}

function switchPlatform(platform) {
  currentPlatform = platform;
  populateGiftSelect(platform);
  loadPlatformConfig(platform);
}

async function loadConfigs() {
  try {
    const [douyinRes, weixinRes] = await Promise.all([
      fetch('../../configs/douyin-muyu.json'),
      fetch('../../configs/shipinhao-muyu.json')
    ]);
    configs.douyin = await douyinRes.json();
    configs.weixin = await weixinRes.json();
  } catch (e) {
    console.error('[木鱼配置] 加载失败:', e);
    showNotification('配置加载失败', 'error');
  }
}

// 填充礼物下拉列表
function populateGiftSelect(platform) {
  const select = document.getElementById('gift-select');
  select.innerHTML = '';

  let gifts = [];
  if (platform === 'douyin' && typeof douyinGifts !== 'undefined') {
    gifts = douyinGifts;
  } else if (platform === 'weixin' && typeof wechatVideoGifts !== 'undefined') {
    gifts = wechatVideoGifts;
  } else {
    // fallback from config
    const config = configs[platform];
    if (config && config.giftList) gifts = config.giftList;
  }

  gifts.forEach(name => {
    const opt = document.createElement('option');
    opt.value = name;
    opt.textContent = name;
    select.appendChild(opt);
  });
}

function loadPlatformConfig(platform) {
  const config = configs[platform];
  if (!config) return;

  // 图片
  if (config.woodfish.customImage) {
    document.getElementById('selected-image-name').textContent =
      config.woodfish.customImage.startsWith('data:') ? '已选择自定义图片' : config.woodfish.customImage.split('/').pop();
  } else {
    document.getElementById('selected-image-name').textContent = '未选择';
  }

  // 触发
  document.getElementById('trigger-gift').checked = config.triggers.gift.enabled;
  document.getElementById('trigger-danmaku').checked = config.triggers.danmaku.enabled;
  document.getElementById('trigger-like').checked = config.triggers.like.enabled;

  // 礼物选中状态
  const select = document.getElementById('gift-select');
  const selected = config.triggers.gift.selectedGifts || [];
  Array.from(select.options).forEach(opt => {
    opt.selected = selected.includes(opt.value);
  });

  // 倍数
  document.getElementById('gift-multiplier').value = config.triggers.gift.multiplier;
  document.getElementById('like-multiplier').value = config.triggers.like.multiplier;

  // 显示
  document.getElementById('merit-text').value = config.display.meritText;
  document.getElementById('text-color').value = config.display.meritTextColor;
  document.getElementById('bg-color').value = config.display.backgroundColor;
  updateColorPreviews();
  updateDisplayPreview();
  updatePreviewImage();
  updateLivePreviewStyle();
}

function setupColorPickers() {
  document.getElementById('text-color').addEventListener('input', (e) => {
    document.getElementById('text-color-preview').textContent = e.target.value;
    updateDisplayPreview();
    updateLivePreviewStyle();
  });
  document.getElementById('bg-color').addEventListener('input', (e) => {
    document.getElementById('bg-color-preview').textContent = e.target.value;
    updateDisplayPreview();
    updateLivePreviewStyle();
  });
}

function updateColorPreviews() {
  document.getElementById('text-color-preview').textContent = document.getElementById('text-color').value;
  document.getElementById('bg-color-preview').textContent = document.getElementById('bg-color').value;
}

function updateDisplayPreview() {
  const meritText = document.getElementById('merit-text').value;
  const textColor = document.getElementById('text-color').value;
  const bgColor = document.getElementById('bg-color').value;
  document.getElementById('preview-display').style.backgroundColor = bgColor;
  const pt = document.getElementById('preview-merit-text');
  pt.style.color = textColor;
  pt.textContent = `${meritText} +1`;
}

function updatePreviewImage() {
  const config = configs[currentPlatform];
  const img = document.getElementById('preview-image');
  const liveImg = document.getElementById('live-muyu-img');
  let src = 'assets/木鱼.png';
  if (config && config.woodfish.enableCustomImage && config.woodfish.customImage) {
    src = config.woodfish.customImage.startsWith('data:') ? config.woodfish.customImage : `../${config.woodfish.customImage}`;
  }
  img.src = src;
  img.style.display = 'block';
  img.onerror = function() { this.style.display = 'none'; };
  liveImg.src = src;
}

function updateLivePreviewStyle() {
  const textColor = document.getElementById('text-color').value;
  const bgColor = document.getElementById('bg-color').value;
  document.getElementById('live-preview').style.backgroundColor = bgColor;
  document.querySelectorAll('#live-preview .stat-val').forEach(el => el.style.color = textColor);
}

// 实时预览
function setupLivePreview() {
  const img = document.getElementById('live-muyu-img');
  img.addEventListener('click', () => {
    liveState.total++;
    img.classList.remove('hit');
    void img.offsetWidth;
    img.classList.add('hit');
    showFloat();
    updateLiveStats();
  });
}

function showFloat() {
  const container = document.getElementById('float-container');
  const meritText = document.getElementById('merit-text').value || '功德';
  const textColor = document.getElementById('text-color').value;
  const el = document.createElement('div');
  el.className = 'float-text';
  el.textContent = `${meritText} +1`;
  el.style.color = textColor;
  el.style.left = `${50 + (Math.random() - 0.5) * 40}%`;
  el.style.transform = 'translateX(-50%)';
  container.appendChild(el);
  setTimeout(() => el.remove(), 1200);
}

function updateLiveStats() {
  document.getElementById('live-total').textContent = liveState.total;
  document.getElementById('live-likes').textContent = liveState.likes;
  document.getElementById('live-gifts').textContent = liveState.gifts;
}

function setupEventListeners() {
  // 图片选择
  document.getElementById('image-file-input').addEventListener('change', (e) => {
    const file = e.target.files[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = (ev) => {
      const config = configs[currentPlatform];
      if (config) {
        config.woodfish.customImage = ev.target.result;
        config.woodfish.enableCustomImage = true;
      }
      document.getElementById('selected-image-name').textContent = file.name;
      updatePreviewImage();
      showNotification('图片已选择', 'success');
    };
    reader.readAsDataURL(file);
  });

  document.getElementById('merit-text').addEventListener('input', () => {
    updateDisplayPreview();
  });

  document.getElementById('btn-save-config').addEventListener('click', saveConfig);
  document.getElementById('btn-apply-config').addEventListener('click', applyConfig);
  document.getElementById('btn-reset-config').addEventListener('click', resetConfig);

  // 浏览器显示 - 各自打开独立页面
  document.getElementById('btn-open-douyin').addEventListener('click', () => {
    window.open('muyu-display.html?platform=douyin', 'muyu-douyin', 'width=420,height=520,resizable=yes');
    showNotification('抖音木鱼显示已打开', 'success');
  });
  document.getElementById('btn-open-weixin').addEventListener('click', () => {
    window.open('muyu-display.html?platform=weixin', 'muyu-weixin', 'width=420,height=520,resizable=yes');
    showNotification('视频号木鱼显示已打开', 'success');
  });
}

function getSelectedGifts() {
  const select = document.getElementById('gift-select');
  return Array.from(select.selectedOptions).map(o => o.value);
}

function saveConfig() {
  const config = configs[currentPlatform];
  if (!config) return;

  config.triggers.gift.enabled = document.getElementById('trigger-gift').checked;
  config.triggers.danmaku.enabled = document.getElementById('trigger-danmaku').checked;
  config.triggers.like.enabled = document.getElementById('trigger-like').checked;
  config.triggers.gift.selectedGifts = getSelectedGifts();
  config.triggers.gift.multiplier = parseFloat(document.getElementById('gift-multiplier').value);
  config.triggers.like.multiplier = parseFloat(document.getElementById('like-multiplier').value);
  config.display.meritText = document.getElementById('merit-text').value;
  config.display.meritTextColor = document.getElementById('text-color').value;
  config.display.backgroundColor = document.getElementById('bg-color').value;

  localStorage.setItem(`muyu-config-${currentPlatform}`, JSON.stringify(config));
  showNotification('配置已保存', 'success');
}

function applyConfig() {
  saveConfig();
  if (window.opener && !window.opener.closed) {
    window.opener.postMessage({ type: 'muyu-config-update', platform: currentPlatform, config: configs[currentPlatform] }, '*');
    showNotification('配置已应用', 'success');
  } else {
    showNotification('配置已保存', 'info');
  }
}

async function resetConfig() {
  if (!confirm('确定重置为默认配置？')) return;
  liveState = { total: 0, likes: 0, gifts: 0 };
  updateLiveStats();
  await loadConfigs();
  switchPlatform(currentPlatform);
  localStorage.removeItem(`muyu-config-${currentPlatform}`);
  showNotification('已重置', 'info');
}

function showNotification(message, type = 'info') {
  const colors = { success: '#00c853', error: '#ff1744', warning: '#ffd600', info: '#2196f3' };
  const el = document.createElement('div');
  el.style.cssText = `position:fixed;top:20px;right:20px;background:${colors[type]};color:white;padding:10px 18px;border-radius:8px;box-shadow:0 4px 12px rgba(0,0,0,0.3);z-index:10000;font-size:14px;animation:slideIn .3s ease;`;
  el.textContent = message;
  document.body.appendChild(el);
  setTimeout(() => { el.style.animation = 'slideOut .3s ease'; setTimeout(() => el.remove(), 300); }, 2500);
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init);
} else {
  init();
}

const style = document.createElement('style');
style.textContent = `
  @keyframes slideIn { from{transform:translateX(100%);opacity:0} to{transform:translateX(0);opacity:1} }
  @keyframes slideOut { from{transform:translateX(0);opacity:1} to{transform:translateX(100%);opacity:0} }
`;
document.head.appendChild(style);
