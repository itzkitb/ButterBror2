import webbrowser
import urllib.parse
import urllib.request
import json
import http.server
import socketserver
import threading
import time
from pathlib import Path

# ><> config
CLIENT_ID = "ur_client_id"
CLIENT_SECRET = "ur_client_secret"
REDIRECT_URI = "http://localhost:17563"

# ><> scopes
SCOPES = "user:bot chat:read chat:edit user:write:chat user:read:chat"

class OAuthCallbackHandler(http.server.BaseHTTPRequestHandler):
    """http request handler to intercept the authorization code from the redirect uri"""
    
    auth_code = None
    auth_error = None

    def do_GET(self):
        parsed_path = urllib.parse.urlparse(self.path)
        query_params = urllib.parse.parse_qs(parsed_path.query)

        if 'code' in query_params:
            OAuthCallbackHandler.auth_code = query_params['code'][0]
            self.send_response(200)
            self.send_header('Content-type', 'text/html; charset=utf-8')
            self.end_headers()
            success_msg = "<h1>success</h1><p>token received. u can close this window and return to the terminal</p>"
            self.wfile.write(success_msg.encode('utf-8'))
        elif 'error' in query_params:
            OAuthCallbackHandler.auth_error = query_params['error'][0]
            self.send_response(400)
            self.send_header('Content-type', 'text/html; charset=utf-8')
            self.end_headers()
            error_msg = f"<h1>error</h1><p>{OAuthCallbackHandler.auth_error}</p>"
            self.wfile.write(error_msg.encode('utf-8'))
        else:
            self.send_response(404)
            self.end_headers()

        threading.Thread(target=self.server.shutdown, daemon=True).start()

    def log_message(self, format, *args):
        pass

def main():
    print("starting twitch authentication process...")
    
    auth_url = (
        f"https://id.twitch.tv/oauth2/authorize"
        f"?response_type=code"
        f"&client_id={CLIENT_ID}"
        f"&redirect_uri={urllib.parse.quote(REDIRECT_URI)}"
        f"&scope={urllib.parse.quote(SCOPES)}"
        f"&force_verify=true"
    )
    
    print(f"open this url:\n{auth_url}\n")

    port = int(REDIRECT_URI.split(':')[-1])
    
    print(f"waiting for twitch response on port {port}...")
    try:
        with socketserver.TCPServer(("localhost", port), OAuthCallbackHandler) as httpd:
            httpd.serve_forever()
    except OSError as e:
        if e.errno == 10048:
            print(f"port {port} is already in use. close other apps using it or change REDIRECT_URI")
        else:
            raise e
        return

    if OAuthCallbackHandler.auth_error:
        print(f"auth error: {OAuthCallbackHandler.auth_error}")
        return

    if not OAuthCallbackHandler.auth_code:
        print("auth code not received. process aborted")
        return

    print("code received. exchanging it for tokens...")

    token_url = "https://id.twitch.tv/oauth2/token"
    post_data = urllib.parse.urlencode({
        "client_id": CLIENT_ID,
        "client_secret": CLIENT_SECRET,
        "code": OAuthCallbackHandler.auth_code,
        "grant_type": "authorization_code",
        "redirect_uri": REDIRECT_URI
    }).encode("utf-8")

    req = urllib.request.Request(token_url, data=post_data, method="POST")
    
    try:
        with urllib.request.urlopen(req) as response:
            token_data = json.loads(response.read().decode())
            
            access_token = token_data.get('access_token')
            refresh_token = token_data.get('refresh_token')
            expires_in = token_data.get('expires_in')
            
            if not access_token or not refresh_token or expires_in is None:
                print("missing required token fields in response")
                print(f"Response: {token_data}")
                return
            
            current_time = int(time.time())
            expiration_timestamp = current_time + expires_in
            
            auth_file_data = {
                "OAuthToken": access_token,
                "RefreshToken": refresh_token,
                "Ttl": expiration_timestamp
            }
            
            script_dir = Path(__file__).parent.resolve()
            output_file = script_dir / "TwitchAuth.json"
            temp_file = output_file.with_suffix('.tmp')
            
            try:
                with open(temp_file, 'w', encoding='utf-8') as f:
                    json.dump(auth_file_data, f, indent=2)
                
                temp_file.replace(output_file)
                
                print("\n" + "="*70)
                print("success. TwitchAuth.json has been created:")
                print(" ")
                print(f"file location: {output_file}")
                print(f"access token:  {access_token[:20]}...")
                print(f"refresh token: {refresh_token[:20]}...")
                print(f"expires In:    {expires_in}s.")
                print(f"expiration:    {expiration_timestamp}")
                print(" ")
                print("next step: move TwitchAuth.json to the bb appdata directory:")
                print("   windows: %APPDATA%\\SillyApps\\ButterBror2\\")
                print("   linux:   ~/.local/share/SillyApps/ButterBror2/")
                print("the bot will detect and import it")
                print("!!! the file will be deleted by the bot after successful import")
                
            except Exception as e:
                print(f"failed to write TwitchAuth.json: {e}")
                if temp_file.exists():
                    temp_file.unlink()
                return
            
    except urllib.error.HTTPError as e:
        print(f"error exchanging token: http {e.code}")
        print(f"server response: {e.read().decode('utf-8')}")
    except Exception as e:
        print(f"unexpected error: {e}")

if __name__ == "__main__":
    main()