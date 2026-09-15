#!/usr/bin/env node
'use strict';

const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..', '..');
const output = path.join(root, 'docs', 'generated', 'document-map.md');
const groups = [
  ['decisions', '全局决策'],
  ['guides/proposed', '提案指导文档'],
  ['guides/approved', '批准指导文档'],
  ['truth', '代码真相'],
  ['archive', '历史归档']
];

function relative(file) {
  return path.relative(root, file).split(path.sep).join('/');
}

function markdownFiles(relativeDirectory) {
  const directory = path.join(root, 'docs', relativeDirectory);
  if (!fs.existsSync(directory)) return [];
  const result = [];
  const visit = current => {
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      const child = path.join(current, entry.name);
      if (entry.isDirectory()) visit(child);
      else if (entry.isFile() && entry.name.endsWith('.md')) result.push(relative(child));
    }
  };
  visit(directory);
  return result.sort();
}

function titleOf(relativeFile) {
  const text = fs.readFileSync(path.join(root, relativeFile), 'utf8');
  const title = text.match(/^#\s+(.+)$/m);
  return title ? title[1].trim() : path.basename(relativeFile);
}

function packageFiles() {
  const directory = path.join(root, 'tasks', 'packages');
  if (!fs.existsSync(directory)) return [];
  return fs.readdirSync(directory, { withFileTypes: true })
    .filter(entry => entry.isFile() && entry.name.endsWith('.json'))
    .map(entry => path.join(directory, entry.name))
    .sort();
}

const issues = [];
const lines = [
  '# 文档与任务索引（自动生成）',
  '',
  '> **禁止手工编辑**：由 `node scripts/docs/generate_document_map.cjs` 生成。文档状态由目录位置推导，不由本文维护。',
  ''
];

for (const [directory, label] of groups) {
  const files = markdownFiles(directory);
  lines.push(`## ${label}`, '');
  if (files.length === 0) {
    lines.push('暂无。', '');
    continue;
  }
  for (const file of files) lines.push(`- [${titleOf(file)}](../../${file}) — \`${file}\``);
  if (directory === 'guides/approved') {
    for (const file of files) {
      const content = fs.readFileSync(path.join(root, file), 'utf8');
      for (const required of ['目标', '非目标', '验收', '实施授权边界']) {
        if (!content.includes(required)) issues.push(`${file}: 批准指导文档缺少“${required}”章节或声明`);
      }
    }
  }
  lines.push('');
}

lines.push('## 任务包', '');
const packages = packageFiles();
if (packages.length === 0) {
  lines.push('暂无可下发任务包。', '');
} else {
  for (const file of packages) {
    const rel = relative(file);
    let pkg;
    try {
      pkg = JSON.parse(fs.readFileSync(file, 'utf8'));
    } catch (error) {
      issues.push(`${rel}: JSON 无法解析（${error.message}）`);
      continue;
    }
    const guide = typeof pkg.primaryGuide === 'string' ? pkg.primaryGuide.replaceAll('\\', '/') : '';
    const guideAbsolute = path.resolve(root, guide);
    const approvedRoot = path.join(root, 'docs', 'guides', 'approved') + path.sep;
    if (!guide.startsWith('docs/guides/approved/') || !guideAbsolute.startsWith(approvedRoot) || !fs.existsSync(guideAbsolute)) {
      issues.push(`${rel}: primaryGuide 必须指向存在的 docs/guides/approved/ 文档`);
    }
    lines.push(`- \`${pkg.packageId || rel}\` → \`${guide || '缺少 primaryGuide'}\`（${rel}）`);
  }
  lines.push('');
}

lines.push('## 检查结果', '');
if (issues.length === 0) lines.push('- 通过：任务包引用均满足目录规则。');
else for (const issue of issues) lines.push(`- 失败：${issue}`);
lines.push('');

fs.writeFileSync(output, `${lines.join('\n').trimEnd()}\n`, 'utf8');
if (issues.length > 0) process.exitCode = 1;
