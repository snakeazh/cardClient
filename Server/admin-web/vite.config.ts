import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

// 开发时把 /v1 代理到本地服务端，避免跨域。
// base 对齐生产 Nginx 的挂载点：页面部署在 https://域名/gm/ 下。
// 注意：路径刻意避开 "admin" 关键字（运营商/安全设备的 DPI 会拦截 admin 路径的请求）。
export default defineConfig({
  base: "/gm/",
  plugins: [vue()],
  server: {
    port: 5174,
    proxy: {
      "/v1": {
        target: "http://localhost:5254",
        changeOrigin: true
      }
    }
  }
});
