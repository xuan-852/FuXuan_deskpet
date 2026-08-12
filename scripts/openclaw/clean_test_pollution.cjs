// 清理测试消息对符玄记忆库/人格文件的污染
// 1. 备份 pet_memory.json / pet_personality.json
// 2. 删除记忆库中测试消息产生的 entries（summary 含「帮我打开浏览器搜索今天的天气」）
// 3. 回退人格 totalInteractions（本批测试 5 条）、移除「已交流60次」里程碑、重算 familiarity
const fs = require('fs');
const path = require('path');

const DATA_ROOT = 'D:/DesktopPetData';
const MEM_FILE = path.join(DATA_ROOT, 'pet_memory.json');
const PER_FILE = path.join(DATA_ROOT, 'pet_personality.json');
const BACKUP_DIR = path.join(DATA_ROOT, '_test_backup_20260808');
const TEST_MARKER = '帮我打开浏览器搜索今天的天气';

// 测试消息数（本批 rowid 26-30 共 5 条，全部 delivered=1 已被桌宠消费）
const TEST_INTERACTIONS = 5;

function backup() {
    fs.mkdirSync(BACKUP_DIR, { recursive: true });
    for (const f of [MEM_FILE, PER_FILE]) {
        const dst = path.join(BACKUP_DIR, path.basename(f) + '.bak');
        fs.copyFileSync(f, dst);
        console.log(`[备份] ${f} -> ${dst}`);
    }
}

function cleanMemory() {
    const data = JSON.parse(fs.readFileSync(MEM_FILE, 'utf8'));
    const before = data.entries.length;
    const removed = [];
    data.entries = data.entries.filter(e => {
        if ((e.summary || '').includes(TEST_MARKER)) {
            removed.push(`${e.timestamp} | ${e.summary.slice(0, 30)}`);
            return false;
        }
        return true;
    });
    // lastReflectionIndex 修正：语义是「上次反思已处理到的条目索引」
    // 删除发生在末尾（index 28/29），删除后最后索引 = entries.Count-1
    data.lastReflectionIndex = Math.min(data.lastReflectionIndex, Math.max(0, data.entries.length - 1));
    fs.writeFileSync(MEM_FILE, JSON.stringify(data, null, 4), 'utf8');
    console.log(`[记忆] 删除 ${removed.length} 条测试记忆 (${before} -> ${data.entries.length})，lastReflectionIndex=${data.lastReflectionIndex}`);
    removed.forEach(r => console.log(`   ✂ ${r}`));
}

function cleanPersonality() {
    const data = JSON.parse(fs.readFileSync(PER_FILE, 'utf8'));
    const oldTotal = data.totalInteractions;
    const newTotal = Math.max(0, oldTotal - TEST_INTERACTIONS);
    data.totalInteractions = newTotal;
    // familiarity 重算：1 - exp(-N/50)
    data.relationship.familiarity = Math.min(1, Math.max(0, 1 - Math.exp(-newTotal / 50)));
    // 移除「已交流60次」里程碑（60 是本批测试期间触发的）
    data.milestones = (data.milestones || []).filter(m => !m.includes('已交流60次'));
    // lastInteractionDate 回退到测试前最后一次真实对话（2026-08-07 20:13 乐鑫国赛 pdf）
    data.lastInteractionDate = '2026-08-07 20:13';
    fs.writeFileSync(PER_FILE, JSON.stringify(data, null, 4), 'utf8');
    console.log(`[人格] totalInteractions ${oldTotal} -> ${newTotal}，familiarity=${data.relationship.familiarity.toFixed(4)}，移除 60 里程碑`);
    console.log(`[人格] lastInteractionDate -> ${data.lastInteractionDate}`);
    console.log(`[提醒] traits/trust/intimacy 的微小微调（量级<0.02）无法精确回滚，属正常波动范围`);
}

backup();
cleanMemory();
cleanPersonality();
console.log('\n[完成] 清理完毕。备份位于 ' + BACKUP_DIR);
