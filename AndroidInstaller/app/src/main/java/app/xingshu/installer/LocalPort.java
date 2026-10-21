package app.xingshu.installer;

/** Extracts only a port. The entered IP never controls the connection host. */
final class LocalPort {
    static int parse(String text) {
        String value=text.trim();
        if(value.matches("[0-9]{1,3}(\\.[0-9]{1,3}){3}:[0-9]{1,5}")) {
            int colon=value.lastIndexOf(':');
            for(String part:value.substring(0,colon).split("\\.")) {
                if(Integer.parseInt(part)>255)throw new IllegalArgumentException("IP 地址格式不正确");
            }
            value=value.substring(colon+1);
        }
        if(!value.matches("[0-9]{1,5}"))throw new IllegalArgumentException("请填写端口数字，或粘贴完整的 IP 地址:端口");
        int port=Integer.parseInt(value);
        if(port<1024||port>65535)throw new IllegalArgumentException("端口应为 1024～65535 的数字");
        return port;
    }
}
