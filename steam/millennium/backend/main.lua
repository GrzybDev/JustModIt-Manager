-- Backend of the app's Millennium plugin: hands the frontend what the app wrote into the plugin's folder and keeps what
-- the frontend reports, for the app to read. The app writes the plugin's name in place of the marker below when it
-- installs the plugin.
local millennium = require("millennium")
local fs = require("fs")

local PLUGIN = "__PLUGIN_NAME__"

local function path_of(name)
    return fs.join(millennium.get_install_path(), "plugins", PLUGIN, name)
end

local function read(name)
    local file = io.open(path_of(name), "rb")
    if not file then
        return ""
    end
    local content = file:read("*a")
    file:close()
    return content
end

---@ffi
--- Returns the texts file the app wrote for the plugin, or an empty string when there is none yet.
---@return string
function texts()
    return read("texts.json")
end

---@ffi
--- Returns what the frontend reported last, also in an earlier start of Steam, or an empty string when it reported nothing.
---@return string
function last_report()
    return read("status.json")
end

---@ffi
--- Saves what the frontend reports: the hooks it placed and the launch options of the games the app asked about.
---@param status string The report as JSON.
---@return boolean
function report(status)
    local file = io.open(path_of("status.json"), "wb")
    if not file then
        return false
    end
    file:write(status)
    file:close()
    return true
end

---@ffi
--- Returns the version of Millennium that loaded the plugin, or an empty string when it does not tell.
---@return string
function millennium_version()
    local ok, version = pcall(millennium.version)
    return ok and version and tostring(version) or ""
end

return {
    on_load = function()
        millennium.ready()
    end,
}
