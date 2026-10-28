#!/usr/bin/env python3
"""Builds basil-rest.postman_collection.json from openapi.v1.json.

The collection checks the HTTP contract only (status, envelope shape, public/token matrix, redirects and the error
statuses); behaviour is tested by the xUnit tests. Run: python3 generate-collection.py
"""
import json, re

spec = json.load(open("openapi.v1.json"))

ENVELOPE = """
const body = pm.response.json();
pm.test('envelope has every member', () => {
  ['success','code','message','data','meta','errors','timestamp'].forEach(k => pm.expect(body).to.have.property(k));
});
pm.test('envelope code equals the HTTP status', () => pm.expect(body.code).to.eql(pm.response.code));
pm.test('success is true below 400', () => pm.expect(body.success).to.eql(pm.response.code < 400));
"""

def script(lines):
    return {"listen": "test", "script": {"type": "text/javascript", "exec": lines.strip("\n").split("\n")}}

def sample_path(path):
    path = re.sub(r"\{(id|otherId|rid)\}", "999999", path)
    path = re.sub(r"\{sid\}", "00000000-0000-0000-0000-000000000000", path)
    path = path.replace("{name}", "%23none").replace("{category}", "runtime").replace("{image}", "none.png") \
               .replace("{entry}", "none").replace("{action}", "collect")
    path = re.sub(r"\{\*\*?action\}", "gc/collect", path)
    return path

def request(name, method, path, token=None, body=None, tests=None, query=""):
    headers = [{"key": "Host", "value": "api.{{domain}}"}]
    if token:
        headers.append({"key": "Authorization", "value": "Bearer " + token})
    req = {"method": method, "header": headers,
           "url": {"raw": "{{baseUrl}}" + path + query, "host": ["{{baseUrl}}"],
                   "path": [p for p in path.split("/") if p], "query": [
                       {"key": q.split("=")[0], "value": q.split("=")[1]} for q in query[1:].split("&") if q]}}
    if body is not None:
        headers.append({"key": "Content-Type", "value": "application/json"})
        req["body"] = {"mode": "raw", "raw": json.dumps(body)}
    return {"name": name, "request": req, "event": [script(tests)] if tests else []}

# --- scenario ---------------------------------------------------------------------------------------------
scenario = [
    request("Login with a wrong password is 401", "POST", "/v1/auth/login",
            body={"username": "{{adminUser}}", "password": "wrong"}, tests=ENVELOPE + """
pm.test('401 invalidCredentials', () => { pm.response.to.have.status(401); pm.expect(body.errors.code).to.eql('invalidCredentials'); });
"""),
    request("Login", "POST", "/v1/auth/login", body={"username": "{{adminUser}}", "password": "{{adminPassword}}"},
            tests=ENVELOPE + """
pm.test('201 with a token', () => { pm.response.to.have.status(201); pm.expect(body.data.token).to.be.a('string'); });
pm.environment.set('token', body.data.token);
pm.environment.set('userId', body.data.userId);
"""),
    request("Session names the signed-in user", "GET", "/v1/auth/session", token="{{token}}", tests=ENVELOPE + """
pm.test('200', () => pm.response.to.have.status(200));
pm.test('userId matches', () => pm.expect(body.data.userId).to.eql(Number(pm.environment.get('userId'))));
"""),
    request("Create a user as staff", "POST", "/v1/users", token="{{token}}",
            body={"username": "ctuser{{$randomInt}}", "password": "pw"}, tests=ENVELOPE + """
pm.test('201', () => pm.response.to.have.status(201));
pm.environment.set('otherId', body.data.id);
"""),
    request("Public: a user is readable without a token", "GET", "/v1/users/{{otherId}}", tests=ENVELOPE + """
pm.test('200 and shape', () => { pm.response.to.have.status(200); ['id','name','country','permissions','effectivePermissions'].forEach(k => pm.expect(body.data).to.have.property(k)); });
"""),
    request("404 for an unknown user", "GET", "/v1/users/999999", tests=ENVELOPE + """
pm.test('404 notFound', () => { pm.response.to.have.status(404); pm.expect(body.errors.code).to.eql('notFound'); });
"""),
    request("422 for a limit above 100", "GET", "/v1/users", token="{{token}}", query="?limit=101", tests=ENVELOPE + """
pm.test('422', () => pm.response.to.have.status(422));
"""),
    request("Paging meta on a list", "GET", "/v1/users", token="{{token}}", query="?limit=2", tests=ENVELOPE + """
pm.test('meta has offset, limit and total', () => ['offset','limit','total'].forEach(k => pm.expect(body.meta).to.have.property(k)));
"""),
    request("409 systemAccount for BasilBot", "DELETE", "/v1/users/1", token="{{token}}", tests=ENVELOPE + """
pm.test('409 systemAccount', () => { pm.response.to.have.status(409); pm.expect(body.errors.code).to.eql('systemAccount'); });
"""),
    request("Sign in as the new user", "POST", "/v1/auth/login", body={"username": "{{otherName}}", "password": "pw"}, tests="""
const body = pm.response.json();
pm.environment.set('otherToken', body.data.token);
"""),
    request("403 with the missing permission mask", "GET", "/v1/settings", token="{{otherToken}}", tests=ENVELOPE + """
pm.test('403 permissionDenied with the mask', () => { pm.response.to.have.status(403); pm.expect(body.errors.code).to.eql('permissionDenied'); pm.expect(body.errors.missing).to.be.a('number'); });
"""),
    request("/v1/me redirects with 307", "GET", "/v1/me", token="{{token}}", tests="""
pm.test('307 with the Location of the own user', () => {
  pm.response.to.have.status(307);
  pm.expect(pm.response.headers.get('Location')).to.eql('/v1/users/' + pm.environment.get('userId'));
});
"""),
    request("/v1/sessions redirects with 307", "GET", "/v1/sessions", token="{{token}}", tests="""
pm.test('307', () => pm.response.to.have.status(307));
pm.test('Location', () => pm.expect(pm.response.headers.get('Location')).to.eql('/v1/users/' + pm.environment.get('userId') + '/sessions'));
"""),
    request("Alias without a token is 401", "GET", "/v1/me", tests="pm.test('401', () => pm.response.to.have.status(401));"),
    request("Logout", "POST", "/v1/auth/logout", token="{{token}}", tests=ENVELOPE + "pm.test('200', () => pm.response.to.have.status(200));"),
    request("Token is revoked after logout", "GET", "/v1/auth/session", token="{{token}}", tests="pm.test('401', () => pm.response.to.have.status(401));"),
]
# The new user is created with a random name; keep it for the sign-in.
scenario[3]["event"][0]["script"]["exec"].append("pm.environment.set('otherName', body.data.name);")
# Re-login as admin after the logout steps for the rate-limit section.
scenario += [
    request("Login again as admin", "POST", "/v1/auth/login", body={"username": "{{adminUser}}", "password": "{{adminPassword}}"}, tests="""
const body = pm.response.json(); pm.environment.set('token', body.data.token);
"""),
    request("Lower the anonymous limit", "PATCH", "/v1/settings/rateLimits", token="{{token}}",
            body={"rest": {"anonymous": {"perIp": {"count": 2, "window": "00:01:00"}}}}, tests="pm.test('200', () => pm.response.to.have.status(200));"),
    request("Anonymous request 1", "GET", "/health"),
    request("Anonymous request 2", "GET", "/health"),
    request("429 with Retry-After in the envelope", "GET", "/health", tests=ENVELOPE + """
pm.test('429 rateLimited', () => { pm.response.to.have.status(429); pm.expect(body.errors.code).to.eql('rateLimited'); pm.expect(pm.response.headers.get('Retry-After')).to.be.ok; });
"""),
    request("Restore the anonymous limit", "PATCH", "/v1/settings/rateLimits", token="{{token}}",
            body={"rest": {"anonymous": {"perIp": {"count": 100000, "window": "00:01:00"}}}}, tests="pm.test('200', () => pm.response.to.have.status(200));"),
]

# --- public / token matrix, from the OpenAPI document -------------------------------------------------------
matrix = []
for path, item in sorted(spec["paths"].items()):
    for method, op in sorted(item.items()):
        token_route = "security" in op
        if method in ("get", "post", "put", "patch", "delete") and (token_route or method == "get" or path == "/v1/auth/login"):
            url = sample_path(path)
            body = {} if method in ("post", "put", "patch") else None
            if token_route:
                tests = "pm.test('without a token the route is 401', () => pm.response.to.have.status(401));"
                name = f"{method.upper()} {path} needs a token"
            else:
                tests = ("pm.test('public route does not answer 401', () => pm.response.to.not.have.status(401));\n"
                         "pm.test('answers inside the documented statuses', () => pm.expect([200,204,307,404,410,422]).to.include(pm.response.code));")
                name = f"{method.upper()} {path} is public"
            matrix.append(request(name, method.upper(), url, body=body, tests=tests))

collection = {
    "info": {"name": "Basil REST v1 contract", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json",
             "description": "Checks the HTTP contract of the REST API (status, envelope, public/token matrix, redirects, error statuses). Behaviour is covered by the xUnit tests. Generated by generate-collection.py from openapi.v1.json."},
    "item": [{"name": "Scenario", "item": scenario}, {"name": "Public and token matrix", "item": matrix}],
}
json.dump(collection, open("basil-rest.postman_collection.json", "w"), indent=2)
json.dump({"name": "Basil local", "values": [
    {"key": "baseUrl", "value": "http://127.0.0.1:5099", "enabled": True},
    {"key": "domain", "value": "basil.local", "enabled": True},
    {"key": "adminUser", "value": "ciadmin", "enabled": True},
    {"key": "adminPassword", "value": "ciadmin-pass", "enabled": True, "type": "secret"}]},
    open("basil-local.postman_environment.json", "w"), indent=2)
print(len(scenario), "scenario requests,", len(matrix), "matrix requests")
