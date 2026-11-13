<script setup lang="ts">
import { onMounted, ref } from "vue";
import * as api from "./api";
import type { AdminLogEntry, PlayerProfile, PlayerSummary } from "./api";

const loggedIn = ref(false);
const loginPassword = ref("");
const keyword = ref("");
const players = ref<api.PlayerSummary[]>([]);
const selected = ref<PlayerProfile | null>(null);
const logs = ref<AdminLogEntry[]>([]);
const goldAmount = ref(1000);
const energyAmount = ref(10);
const message = ref("");
const messageOk = ref(true);
const busy = ref(false);

async function login() {
  await guard(async () => {
    const res = await api.login(loginPassword.value);
    localStorage.setItem("admin-token", res.token);
    api.setToken(res.token);
    loggedIn.value = true;
    loginPassword.value = "";
    await refreshAll();
  });
}

function logout() {
  localStorage.removeItem("admin-token");
  api.setToken("");
  loggedIn.value = false;
  selected.value = null;
  players.value = [];
  logs.value = [];
}

async function guard(fn: () => Promise<void>) {
  busy.value = true;
  message.value = "";
  try {
    await fn();
  } catch (e) {
    message.value = e instanceof Error ? e.message : String(e);
    messageOk.value = false;
    if (e instanceof api.AuthError && loggedIn.value) {
      logout();
    }
  } finally {
    busy.value = false;
  }
}

async function search() {
  await guard(async () => {
    players.value = await api.searchPlayers(keyword.value);
  });
}

async function select(p: PlayerSummary) {
  await guard(async () => {
    selected.value = await api.getPlayer(p.userId);
  });
}

async function runGrant(fn: () => Promise<PlayerProfile>, ok: string) {
  if (!selected.value) {
    return;
  }
  await guard(async () => {
    selected.value = await fn();
    message.value = ok;
    messageOk.value = true;
    await refreshLogs();
  });
}

async function refreshLogs() {
  logs.value = await api.listLogs();
}

async function refreshAll() {
  await guard(async () => {
    await search();
    await refreshLogs();
    if (selected.value) {
      selected.value = await api.getPlayer(selected.value.userId);
    }
  });
}

function fmtTime(iso: string): string {
  return new Date(iso).toLocaleString();
}

function short(id: string): string {
  return id.replace(/-/g, "").slice(0, 8);
}

onMounted(() => {
  const saved = localStorage.getItem("admin-token") ?? "";
  if (saved) {
    api.setToken(saved);
    loggedIn.value = true;
    refreshAll();
  }
});
</script>

<template>
  <div class="page">
    <div v-if="!loggedIn" class="login-wrap">
      <div class="panel login-card">
        <h1>天选勇士后台</h1>
        <p class="login-tip">请输入管理员密码</p>
        <input
          v-model="loginPassword"
          type="password"
          placeholder="管理员密码"
          class="login-input"
          @keyup.enter="login"
        />
        <button class="primary login-btn" @click="login" :disabled="busy || !loginPassword">登 录</button>
        <p v-if="message" class="message err-msg">{{ message }}</p>
      </div>
    </div>

    <template v-else>
      <header>
        <h1>天选勇士后台</h1>
        <div class="token-bar">
          <button @click="refreshAll" :disabled="busy">刷新</button>
          <button class="danger" @click="logout">退出登录</button>
        </div>
      </header>

      <p v-if="message" class="message" :class="messageOk ? 'ok-msg' : 'err-msg'">{{ message }}</p>

      <main class="layout">
        <section class="panel">
          <h2>玩家搜索</h2>
          <div class="search-bar">
            <input
              v-model="keyword"
              placeholder="昵称 / UserId 片段，空 = 最近活跃"
              @keyup.enter="search"
            />
            <button @click="search" :disabled="busy">搜索</button>
          </div>
          <table v-if="players.length">
            <thead>
              <tr>
                <th>玩家</th>
                <th>金币</th>
                <th>体力</th>
                <th>PVP 分</th>
                <th>更新时间</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="p in players"
                :key="p.userId"
                :class="{ active: selected?.userId === p.userId }"
                @click="select(p)"
              >
                <td>
                  <div class="nick">{{ p.nickName || "(未命名)" }}</div>
                  <div class="uid">{{ short(p.userId) }}</div>
                </td>
                <td>{{ p.gold }}</td>
                <td>{{ p.energy }}</td>
                <td>{{ p.pvpScore }}</td>
                <td class="time">{{ fmtTime(p.updatedAt) }}</td>
              </tr>
            </tbody>
          </table>
          <p v-else class="empty">暂无结果</p>
        </section>

        <section class="panel" v-if="selected">
          <h2>玩家详情 <span class="uid">{{ selected.userId }}</span></h2>
          <div class="stats">
            <div><label>昵称</label>{{ selected.nickName || "(未命名)" }}</div>
            <div><label>金币</label>{{ selected.gold }}</div>
            <div><label>体力</label>{{ selected.energy.current }}</div>
            <div><label>PVP 分</label>{{ selected.pvpScore }}</div>
            <div><label>已解锁英雄</label>{{ selected.level.unlockedHeroIds.length }}</div>
            <div><label>天赋种类</label>{{ selected.talent.entries.length }}</div>
            <div><label>天赋抽取次数</label>{{ selected.talent.drawCount }}</div>
            <div>
              <label>难度进度</label>
              {{ selected.level.difficultyProgress.length
                ? selected.level.difficultyProgress
                    .map(d => `${d.difficulty}-${d.highestClearedLevel}`)
                    .join(" / ")
                : "无" }}
            </div>
          </div>

          <h3>发放</h3>
          <div class="ops">
            <input v-model.number="goldAmount" type="number" min="1" class="amount" />
            <button class="primary" @click="runGrant(() => api.grantGold(selected!.userId, goldAmount), '金币已发放')" :disabled="busy">
              发金币
            </button>
            <input v-model.number="energyAmount" type="number" min="1" class="amount" />
            <button class="primary" @click="runGrant(() => api.grantEnergy(selected!.userId, energyAmount), '体力已发放')" :disabled="busy">
              发体力
            </button>
          </div>

          <h3>解锁</h3>
          <div class="ops">
            <button class="danger" @click="runGrant(() => api.unlockAll(selected!.userId, 'heroes'), '已解锁全部英雄')" :disabled="busy">
              解锁全部英雄
            </button>
            <button class="danger" @click="runGrant(() => api.unlockAll(selected!.userId, 'talents'), '已解锁全部天赋')" :disabled="busy">
              解锁全部天赋
            </button>
            <button class="danger" @click="runGrant(() => api.unlockAll(selected!.userId, 'levels'), '已解锁全部难度关卡')" :disabled="busy">
              解锁全部难度关卡
            </button>
          </div>
        </section>

        <section class="panel">
          <h2>操作记录 <button class="small" @click="refreshLogs" :disabled="busy">刷新</button></h2>
          <table v-if="logs.length">
            <thead>
              <tr>
                <th>时间</th>
                <th>命令</th>
                <th>目标</th>
                <th>参数</th>
                <th>IP</th>
                <th>结果</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="l in logs" :key="l.id">
                <td class="time">{{ fmtTime(l.createdAtUtc) }}</td>
                <td>{{ l.command }}</td>
                <td class="uid">{{ short(l.targetUserId) }}</td>
                <td>{{ l.detail }}</td>
                <td>{{ l.remoteIp }}</td>
                <td><span class="badge" :class="l.succeeded ? 'badge-ok' : 'badge-fail'">{{ l.succeeded ? "成功" : "失败" }}</span></td>
              </tr>
            </tbody>
          </table>
          <p v-else class="empty">暂无记录</p>
        </section>
      </main>
    </template>
  </div>
</template>
