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

export interface ParsedApiError {
  code: string;
  message: string;
  status?: number;
}

type ErrorWithApiCode = Error & {
  apiCode?: string;
  apiMessage?: string;
  apiStatus?: number;
};

export function parseApiError(error: unknown): ParsedApiError | null {
  if (!error) return null;
  const e = error as ErrorWithApiCode;
  if (typeof e.apiCode === 'string' && typeof e.apiMessage === 'string') {
    return { code: e.apiCode, message: e.apiMessage, status: e.apiStatus };
  }

  if (axios.isAxiosError<AxiosErrorBody>(error)) {
    const code = error.response?.data?.code;
    const msg = error.response?.data?.message;
    if (typeof code === 'string' && typeof msg === 'string') {
      return { code, message: msg, status: error.response?.status };
    }
  }

  return null;
}

http.interceptors.response.use(
  (response: AxiosResponse<ApiResponse<unknown>>) => {
    const body = response.data;
    if (body && body.success === false) {
      const code = body.code || 'UNKNOWN';
      const apiError: ErrorWithApiCode = new Error(body.message);
      apiError.apiCode = code;
      apiError.apiMessage = body.message;
      apiError.apiStatus = response.status;
      if (code !== 'PRE_006') {
        message.error(`${code}：${body.message}`);
      }
      return Promise.reject(apiError);
    }
    return response;
  },
  (error: AxiosError<AxiosErrorBody>) => {
    if (error.response) {
      const data = error.response.data;
      const code = data?.code ?? `HTTP_${error.response.status}`;
      const msg = data?.message ?? error.message;
      if (code !== 'PRE_006') {
        message.error(`${code}：${msg}`);
      }
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
