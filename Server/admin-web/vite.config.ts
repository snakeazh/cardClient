import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

// 开发时把 /v1 代理到本地服务端，避免跨域。
// base 对齐生产 Nginx 的挂载点：页面部署在 https://域名/admin/ 下。
export default defineConfig({
  base: "/admin/",
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
