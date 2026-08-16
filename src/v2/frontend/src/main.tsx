import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import '@ant-design/v5-patch-for-react-19'
import { ConfigProvider } from 'antd'
import zhCN from 'antd/locale/zh_CN'
import './index.css'
import App from './App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ConfigProvider
      locale={zhCN}
      theme={{
        token: {
          colorPrimary: '#1677ff',
          colorInfo: '#1677ff',
          colorSuccess: '#389e0d',
          colorWarning: '#d48806',
          colorError: '#cf1322',
          borderRadius: 6,
          fontFamily: "'Noto Sans SC', 'IBM Plex Sans', sans-serif",
        },
        components: {
          Button: { controlHeight: 32 },
          Menu: { itemBorderRadius: 6, itemHeight: 40 },
        },
      }}
    >
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </ConfigProvider>
  </StrictMode>,
)
