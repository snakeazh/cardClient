// GM 后台 API 封装：所有请求带 X-Admin-Token，错误统一抛 Error(message)。

export interface PlayerSummary {
  userId: string;
  nickName: string;
  gold: number;
  energy: number;
  pvpScore: number;
  updatedAt: string;
}

export interface AdminLogEntry {
  id: number;
  createdAtUtc: string;
  command: string;
  targetUserId: string;
  detail: string;
  remoteIp: string;
  succeeded: boolean;
}

export interface PlayerProfile {
  userId: string;
  nickName: string;
  gold: number;
  pvpScore: number;
  energy: { current: number; adRefillCount: number };
  level: {
    difficultyProgress: { difficulty: number; highestClearedLevel: number }[];
    unlockedHeroIds: number[];
    lastHeroId: number;
    lastDifficulty: number;
  };
  talent: { entries: { talentId: number; count: number }[]; drawCount: number };
  bag: { itemId: number; count: number }[];
}

let token = "";

export function setToken(t: string) {
  token = t;
}

export class AuthError extends Error {}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const resp = await fetch(path, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      "X-Admin-Token": token
    }
  });
  if (!resp.ok) {
    if (resp.status === 401) {
      throw new AuthError("密码错误或登录已失效");
    }
    let message = `HTTP ${resp.status}`;
    try {
      const body = await resp.json();
      if (body?.message) {
        message = body.message;
      }
    } catch {
      // 非 JSON 响应，保留 HTTP 状态码描述
    }
    throw new Error(message);
  }
  return (await resp.json()) as T;
}

export function login(password: string): Promise<{ token: string }> {
  return request("/v1/gm/login", { method: "POST", body: JSON.stringify({ password }) });
}

export function searchPlayers(keyword: string): Promise<PlayerSummary[]> {
  return request(`/v1/gm/players?keyword=${encodeURIComponent(keyword)}&take=20`);
}

export function getPlayer(userId: string): Promise<PlayerProfile> {
  return request(`/v1/gm/player?userId=${encodeURIComponent(userId)}`);
}

export function listLogs(): Promise<AdminLogEntry[]> {
  return request("/v1/gm/logs?take=50");
}

export function grantGold(userId: string, amount: number): Promise<PlayerProfile> {
  return request("/v1/gm/grant-gold", { method: "POST", body: JSON.stringify({ userId, amount }) });
}

export function grantEnergy(userId: string, amount: number): Promise<PlayerProfile> {
  return request("/v1/gm/grant-energy", { method: "POST", body: JSON.stringify({ userId, amount }) });
}

export function unlockAll(userId: string, what: "heroes" | "talents" | "levels"): Promise<PlayerProfile> {
  return request(`/v1/gm/unlock-all-${what}`, { method: "POST", body: JSON.stringify({ userId }) });
}
