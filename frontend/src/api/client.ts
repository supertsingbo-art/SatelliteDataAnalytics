import axios, { AxiosError, AxiosResponse } from 'axios';
import { message } from 'antd';

export interface ApiResponse<T> {
  success: boolean;
  code: string;
  message: string;
  data: T | null;
  traceId: string;
}

const TOKEN_KEY = 'satdata.access_token';

export function setAccessToken(token: string | null) {
  if (token) {
    localStorage.setItem(TOKEN_KEY, token);
  } else {
    localStorage.removeItem(TOKEN_KEY);
  }
}

export function getAccessToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export const http = axios.create({
  baseURL: '',
  timeout: 30_000
});

http.interceptors.request.use((config) => {
  const token = getAccessToken();
  if (token) {
    config.headers = config.headers ?? {};
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

interface AxiosErrorBody {
  success: boolean;
  code: string;
  message: string;
}

http.interceptors.response.use(
  (response: AxiosResponse<ApiResponse<unknown>>) => {
    const body = response.data;
    if (body && body.success === false) {
      const code = body.code || 'UNKNOWN';
      message.error(`${code}：${body.message}`);
      return Promise.reject(new Error(body.message));
    }
    return response;
  },
  (error: AxiosError<AxiosErrorBody>) => {
    if (error.response) {
      const data = error.response.data;
      const code = data?.code ?? `HTTP_${error.response.status}`;
      const msg = data?.message ?? error.message;
      message.error(`${code}：${msg}`);
    } else {
      message.error(error.message || '网络异常');
    }
    return Promise.reject(error);
  }
);

export async function request<T>(
  method: 'get' | 'post' | 'put' | 'delete' | 'patch',
  url: string,
  body?: unknown,
  params?: Record<string, unknown>
): Promise<T> {
  const response = await http.request<ApiResponse<T>>({
    method,
    url,
    data: body,
    params
  });
  if (!response.data.success) {
    throw new Error(response.data.message);
  }
  return response.data.data as T;
}
