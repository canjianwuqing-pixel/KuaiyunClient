# 账号功能测试

本分支新增以下账号功能：

1. 邮箱密码登录。
2. 记住账号。
3. 自动登录；密码使用 Windows DPAPI 加密后保存，仅当前 Windows 用户可解密。
4. 邮箱验证码注册，可填写邀请码。
5. 邮箱验证码找回密码。
6. 主动退出账号时关闭自动登录并删除已保存密码。

## V2Board 接口

- `POST /api/v1/passport/comm/sendEmailVerify`
- `POST /api/v1/passport/auth/register`
- `POST /api/v1/passport/auth/forget`
- 原有登录接口保持不变。

## 人工测试重点

- 注册验证码倒计时是否正常。
- 后台启用 reCAPTCHA 时，注册接口返回的提示是否能直接显示。
- 勾选自动登录后重启客户端是否能进入首页。
- 点击退出后再次启动是否停留在登录页。
